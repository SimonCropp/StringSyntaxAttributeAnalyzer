namespace StringSyntaxAttributeAnalyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(AddStringSyntaxCodeFixProvider))]
[Shared]
public class AddStringSyntaxCodeFixProvider : CodeFixProvider
{
    // Kept in sync with MismatchAnalyzer. Duplicated instead of shared to keep
    // the analyzer project free of a back-reference from the codefix project.
    const string valueKey = "StringSyntaxValue";
    const string missingSourceFormatId = "SSA002";
    const string droppedFormatId = "SSA003";
    const string equalityMissingFormatId = "SSA005";

    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        missingSourceFormatId,
        droppedFormatId,
        equalityMissingFormatId
    ];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.AdditionalLocations.Count == 0)
            {
                continue;
            }

            if (!diagnostic.Properties.TryGetValue(valueKey, out var value) ||
                value is null)
            {
                continue;
            }

            var declarationLocation = diagnostic.AdditionalLocations[0];
            if (!declarationLocation.IsInSource)
            {
                continue;
            }

            var declarationTree = declarationLocation.SourceTree;
            if (declarationTree is null)
            {
                continue;
            }

            var declarationRoot = await declarationTree
                .GetRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            var declarationNode = declarationRoot.FindNode(declarationLocation.SourceSpan);
            if (FindAttributeHost(declarationNode) is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Add [Syntax(\"{value}\")]",
                    cancel => AddAttributeAsync(
                        context.Document.Project.Solution,
                        declarationLocation,
                        value,
                        cancel),
                    equivalenceKey: $"AddSyntax:{value}"),
                diagnostic);
        }
    }

    static async Task<Solution> AddAttributeAsync(
        Solution solution,
        Location declarationLocation,
        string value,
        Cancel cancel)
    {
        var document = solution.GetDocument(declarationLocation.SourceTree);
        if (document is null)
        {
            return solution;
        }

        var root = await document
            .GetSyntaxRootAsync(cancel)
            .ConfigureAwait(false);
        if (root is null)
        {
            return solution;
        }

        var declarationNode = root.FindNode(declarationLocation.SourceSpan);
        var targetNode = FindAttributeHost(declarationNode);
        if (targetNode is null)
        {
            return solution;
        }

        var compilation = await document.Project.GetCompilationAsync(cancel).ConfigureAwait(false);
        var attributeName = ResolveAttributeName(compilation);

        var newTargetNode = AddStringSyntaxAttribute(targetNode, value, attributeName);
        if (newTargetNode is null)
        {
            return solution;
        }

        var newRoot = root.ReplaceNode(targetNode, newTargetNode);
        var newDocument = document.WithSyntaxRoot(newRoot);

        // Deliberately no ImportAdder / Simplifier pass. The attribute is inserted as the
        // short name `StringSyntax` and resolves via whatever using (file-local or
        // `global using`) the consumer already has. Adding an explicit using here was
        // fighting with Rider/VS "remove unnecessary usings" cleanup when a global using
        // was in scope — each successive fix left behind a blank line of trivia where the
        // redundant local using had been.
        newDocument = await Formatter
            .FormatAsync(newDocument, Formatter.Annotation, cancellationToken: cancel)
            .ConfigureAwait(false);

        return newDocument.Project.Solution;
    }

    static SyntaxNode? FindAttributeHost(SyntaxNode node)
    {
        // IFieldSymbol.DeclaringSyntaxReferences points at the VariableDeclaratorSyntax
        // (e.g. `a` in `public string a, b;`). Attribute lists live on the enclosing
        // FieldDeclarationSyntax, which would apply the attribute to *all* declarators.
        var declarator = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (declarator is not null)
        {
            if (declarator.Parent is VariableDeclarationSyntax { Variables.Count: > 1 })
            {
                return null;
            }

            return declarator.FirstAncestorOrSelf<FieldDeclarationSyntax>();
        }

        return node.FirstAncestorOrSelf<SyntaxNode>(ancestor =>
            ancestor is PropertyDeclarationSyntax or ParameterSyntax);
    }

    static SyntaxNode? AddStringSyntaxAttribute(SyntaxNode host, string value, string attributeName)
    {
        var argument = AttributeArgument(
            LiteralExpression(
                SyntaxKind.StringLiteralExpression,
                Literal(value)));

        var attribute = Attribute(IdentifierName(attributeName))
            .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)));

        var attributeList = AttributeList(SingletonSeparatedList(attribute))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return host switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(attributeList),
            FieldDeclarationSyntax field => field.AddAttributeLists(attributeList),
            ParameterSyntax parameter => parameter.AddAttributeLists(attributeList),
            _ => null
        };
    }

    // Prefer `[Syntax(...)]` when the generator's `global using SyntaxAttribute = ...`
    // alias is in scope. Fall back to `[StringSyntax(...)]` when the consumer has opted
    // out of the global usings (via StringSyntaxAnalyzer_EmitGlobalUsings=false).
    static string ResolveAttributeName(Compilation? compilation)
    {
        if (compilation is null)
        {
            return "StringSyntax";
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var usingDirective in root.DescendantNodes(_ => _ is CompilationUnitSyntax or NamespaceDeclarationSyntax or FileScopedNamespaceDeclarationSyntax).OfType<UsingDirectiveSyntax>())
            {
                if (usingDirective.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) &&
                    usingDirective.Alias?.Name.Identifier.ValueText == "SyntaxAttribute")
                {
                    return "Syntax";
                }
            }
        }

        return "StringSyntax";
    }
}

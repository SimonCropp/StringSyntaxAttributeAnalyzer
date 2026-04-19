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
    const string redundantStringSyntaxId = "SSA007";

    public override ImmutableArray<string> FixableDiagnosticIds =>
    [
        missingSourceFormatId,
        droppedFormatId,
        equalityMissingFormatId,
        redundantStringSyntaxId
    ];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var compilation = await context.Document.Project
            .GetCompilationAsync(context.CancellationToken)
            .ConfigureAwait(false);

        foreach (var diagnostic in context.Diagnostics)
        {
            if (diagnostic.Id == redundantStringSyntaxId)
            {
                await ShortcutReplacer.RegisterAsync(context, diagnostic).ConfigureAwait(false);
                continue;
            }

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

            var host = AttributeHost.Find(declarationNode);
            if (host is null)
            {
                continue;
            }

            // Normalize each raw value to its canonical PascalCase form (e.g. `"html"`
            // → `"Html"`) so downstream `IsKnown`/shortcut/`Syntax.X` lookups succeed
            // for case-insensitive variants. Unknown values pass through unchanged so
            // custom format strings still emit as literals.
            var values = value
                .Split('|')
                .Select(_ => KnownSyntaxConstants.TryGetCanonical(_, out var canonical) ? canonical : _)
                .ToArray();

            // For a union source (multiple values), offer one fix per option — and, when
            // the host can carry UnionSyntax (property/field/parameter), an additional
            // "matching union" fix that emits all options. Method/return and local
            // language-comment hosts can't express a union, so we only surface the
            // per-value fixes there.
            if (values.Length > 1 && AttributeHost.CanHostUnion(host))
            {
                var (unionTitle, unionKey) = BuildUnionFixMetadata(host, values);
                context.RegisterCodeFix(
                    CodeAction.Create(
                        unionTitle,
                        cancel => AddAttributeAsync(
                            context.Document.Project.Solution,
                            declarationLocation,
                            values,
                            cancel),
                        equivalenceKey: unionKey),
                    diagnostic);
            }

            foreach (var singleValue in values)
            {
                var useShortcut = CanUseShortcut(compilation, host, singleValue);
                var (title, equivalenceKey) = BuildFixMetadata(host, singleValue, useShortcut);
                context.RegisterCodeFix(
                    CodeAction.Create(
                        title,
                        cancel => AddAttributeAsync(
                            context.Document.Project.Solution,
                            declarationLocation,
                            [singleValue],
                            cancel),
                        equivalenceKey: equivalenceKey),
                    diagnostic);
            }
        }
    }

    static async Task<Solution> AddAttributeAsync(
        Solution solution,
        Location location,
        string[] values,
        Cancel cancel)
    {
        var document = solution.GetDocument(location.SourceTree);
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

        var declarationNode = root.FindNode(location.SourceSpan);
        var targetNode = AttributeHost.Find(declarationNode);
        if (targetNode is null)
        {
            return solution;
        }

        SyntaxNode? newTargetNode;
        if (targetNode is LocalDeclarationStatementSyntax localHost)
        {
            newTargetNode = AttributeNodeBuilder.AddLanguageCommentToLocal(localHost, values[0]);
        }
        else if (values.Length > 1)
        {
            newTargetNode = AttributeNodeBuilder.AddUnionSyntax(targetNode, values);
        }
        else
        {
            var compilation = await document.Project.GetCompilationAsync(cancel).ConfigureAwait(false);
            if (CanUseShortcut(compilation, targetNode, values[0]))
            {
                // Shortcut attributes (`[Html]`, `[Regex]`, ...) are source-generated
                // into the consumer's compilation when they opt in with
                // `StringSyntaxAnalyzer_EmitShortcutAttributes=true`. When available,
                // prefer them — they are what the user configured the project to use.
                newTargetNode = AttributeNodeBuilder.AddParameterless(targetNode, values[0]);
            }
            else
            {
                var resolvedName = ResolveAttributeName(compilation);
                var attributeName = AttributeHost.IsMethod(targetNode)
                    ? "ReturnSyntax"
                    : resolvedName;
                // Emit `Syntax.X` only when the short alias is available — the `Syntax`
                // class lives in the StringSyntaxAttributeAnalyzer namespace, which the
                // generator's global usings bring into scope alongside the alias. When
                // the consumer has opted out of globals, fall back to a literal so the
                // output compiles without a manual using directive.
                var useConstant = resolvedName == "Syntax" && KnownSyntaxConstants.IsKnown(values[0]);
                newTargetNode = AttributeNodeBuilder.AddStringSyntax(targetNode, values[0], attributeName, useConstant);
            }
        }

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

    static (string Title, string EquivalenceKey) BuildFixMetadata(
        SyntaxNode? host,
        string value,
        bool useShortcut = false)
    {
        var description = host is null ? "declaration" : HostDescription.Describe(host);
        // Known values surface as `Syntax.X` so the user gets a named constant rather
        // than a bare string literal; unknown values (e.g. "custom-format") fall back
        // to a literal. Titles mirror what the fix actually writes.
        if (useShortcut)
        {
            return (
                $"Add [{value}] to {description}",
                $"AddShortcut:{value}");
        }
        var argument = AttributeNodeBuilder.FormatArgument(value);
        return host switch
        {
            LocalDeclarationStatementSyntax => (
                $"Add //language={AttributeNodeBuilder.ToRiderToken(value)} to {description}",
                $"AddLanguageComment:{value}"),
            _ when AttributeHost.IsMethod(host) => (
                $"Add [ReturnSyntax({argument})] to {description}",
                $"AddReturnSyntax:{value}"),
            _ => (
                $"Add [Syntax({argument})] to {description}",
                $"AddSyntax:{value}")
        };
    }

    static (string Title, string EquivalenceKey) BuildUnionFixMetadata(SyntaxNode? host, string[] values)
    {
        var description = host is null ? "declaration" : HostDescription.Describe(host);
        var argumentList = string.Join(", ", values.Select(AttributeNodeBuilder.FormatArgument));
        var attributeName = AttributeHost.IsMethod(host) ? "ReturnSyntax" : "UnionSyntax";
        return (
            $"Add [{attributeName}({argumentList})] to {description}",
            $"Add{attributeName}:{string.Join('|', values)}");
    }

    // A shortcut attribute like `[Html]` is usable when:
    //   - the consumer opted in, so the generator emitted the type (detected by
    //     looking up `StringSyntaxAttributeAnalyzer.<Name>Attribute` in the
    //     compilation);
    //   - the host can carry an attribute whose target is Field/Property/Parameter
    //     (the shortcut's AttributeUsage doesn't include methods or return values);
    //   - the value is a known constant (shortcuts are only generated for those).
    static bool CanUseShortcut(Compilation? compilation, SyntaxNode? host, string value)
    {
        if (compilation is null)
        {
            return false;
        }

        if (host is null ||
            AttributeHost.IsMethod(host) ||
            host is LocalDeclarationStatementSyntax)
        {
            return false;
        }

        if (!KnownSyntaxConstants.IsKnown(value))
        {
            return false;
        }

        return compilation.GetTypeByMetadataName($"StringSyntaxAttributeAnalyzer.{value}Attribute") is not null;
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
            foreach (var directive in root
                         .DescendantNodes(_ => _ is
                             CompilationUnitSyntax or
                             NamespaceDeclarationSyntax or
                             FileScopedNamespaceDeclarationSyntax)
                         .OfType<UsingDirectiveSyntax>())
            {
                if (directive.GlobalKeyword.IsKind(SyntaxKind.GlobalKeyword) &&
                    directive.Alias?.Name.Identifier.ValueText == "SyntaxAttribute")
                {
                    return "Syntax";
                }
            }
        }

        return "StringSyntax";
    }
}

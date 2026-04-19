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
                await RegisterReplaceWithShortcut(context, diagnostic).ConfigureAwait(false);
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

            if (FindAttributeHost(declarationNode) is null)
            {
                continue;
            }

            var host = FindAttributeHost(declarationNode);
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
            if (values.Length > 1 && CanHostUnion(host))
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

    static async Task RegisterReplaceWithShortcut(CodeFixContext context, Diagnostic diagnostic)
    {
        if (!diagnostic.Properties.TryGetValue(valueKey, out var value) || value is null)
        {
            return;
        }

        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);
        var isReturn = root is not null && IsMethodAttribute(root, diagnostic.Location);
        var title = isReturn
            ? $"Replace with [return: {value}]"
            : $"Replace with [{value}]";

        context.RegisterCodeFix(
            CodeAction.Create(
                title,
                cancel => ReplaceWithShortcutAsync(
                    context.Document.Project.Solution,
                    diagnostic.Location,
                    value,
                    cancel),
                equivalenceKey: $"ReplaceWithShortcut:{value}"),
            diagnostic);
    }

    static bool IsMethodAttribute(SyntaxNode root, Location location)
    {
        var node = root.FindNode(location.SourceSpan);
        var attribute = node as AttributeSyntax ?? node.FirstAncestorOrSelf<AttributeSyntax>();
        return attribute?.FirstAncestorOrSelf<AttributeListSyntax>()?.Parent is
            MethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            DelegateDeclarationSyntax;
    }

    static async Task<Solution> ReplaceWithShortcutAsync(
        Solution solution,
        Location location,
        string name,
        Cancel cancel)
    {
        var document = solution.GetDocument(location.SourceTree);
        if (document is null)
        {
            return solution;
        }

        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        if (root is null)
        {
            return solution;
        }

        // The diagnostic location is the `[StringSyntax(...)]` (or `[ReturnSyntax(...)]`)
        // AttributeSyntax itself.
        var node = root.FindNode(location.SourceSpan);
        var attribute = node as AttributeSyntax ?? node.FirstAncestorOrSelf<AttributeSyntax>();
        if (attribute is null)
        {
            return solution;
        }

        var list = attribute.FirstAncestorOrSelf<AttributeListSyntax>();
        SyntaxNode newRoot;
        if (list is not null &&
            list.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax or DelegateDeclarationSyntax &&
            list.Attributes.Count == 1)
        {
            // Shortcut attributes on methods compile as `[return: Name]` — their
            // AttributeUsage targets ReturnValue, not Method. Rebuild the whole list
            // with the `return:` target specifier so the output is legal C#.
            var replacementList = AttributeList(SingletonSeparatedList(Attribute(IdentifierName(name))))
                .WithTarget(AttributeTargetSpecifier(Token(SyntaxKind.ReturnKeyword)))
                .WithLeadingTrivia(list.GetLeadingTrivia())
                .WithTrailingTrivia(list.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);
            newRoot = root.ReplaceNode(list, replacementList);
        }
        else
        {
            var replacement = Attribute(IdentifierName(name))
                .WithLeadingTrivia(attribute.GetLeadingTrivia())
                .WithTrailingTrivia(attribute.GetTrailingTrivia())
                .WithAdditionalAnnotations(Formatter.Annotation);

            newRoot = root.ReplaceNode(attribute, replacement);
        }
        var newDocument = await Formatter
            .FormatAsync(document.WithSyntaxRoot(newRoot), Formatter.Annotation, cancellationToken: cancel)
            .ConfigureAwait(false);
        return newDocument.Project.Solution;
    }

    static bool CanHostUnion(SyntaxNode? host) =>
        host is
            PropertyDeclarationSyntax or
            FieldDeclarationSyntax or
            ParameterSyntax or
            MethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            DelegateDeclarationSyntax;

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
        var targetNode = FindAttributeHost(declarationNode);
        if (targetNode is null)
        {
            return solution;
        }

        SyntaxNode? newTargetNode;
        if (targetNode is LocalDeclarationStatementSyntax localHost)
        {
            newTargetNode = AddLanguageCommentToLocal(localHost, values[0]);
        }
        else if (values.Length > 1)
        {
            newTargetNode = AddUnionSyntaxAttribute(targetNode, values);
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
                newTargetNode = AddParameterlessAttribute(targetNode, values[0]);
            }
            else
            {
                var resolvedName = ResolveAttributeName(compilation);
                var attributeName = IsMethodHost(targetNode)
                    ? "ReturnSyntax"
                    : resolvedName;
                // Emit `Syntax.X` only when the short alias is available — the `Syntax`
                // class lives in the StringSyntaxAttributeAnalyzer namespace, which the
                // generator's global usings bring into scope alongside the alias. When
                // the consumer has opted out of globals, fall back to a literal so the
                // output compiles without a manual using directive.
                var useConstant = resolvedName == "Syntax" && KnownSyntaxConstants.IsKnown(values[0]);
                newTargetNode = AddStringSyntaxAttribute(targetNode, values[0], attributeName, useConstant);
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

    static SyntaxNode? FindAttributeHost(SyntaxNode node)
    {
        // Both IFieldSymbol and ILocalSymbol DeclaringSyntaxReferences point at the
        // VariableDeclaratorSyntax (e.g. `a` in `public string a, b;` or `var a = 1`).
        // The host depends on context: FieldDeclarationSyntax for a field,
        // LocalDeclarationStatementSyntax for a local. Multi-declarator forms are
        // refused in both cases — one attribute or comment would apply to all.
        var declarator = node.FirstAncestorOrSelf<VariableDeclaratorSyntax>();
        if (declarator is not null)
        {
            if (declarator.Parent is VariableDeclarationSyntax { Variables.Count: > 1 })
            {
                return null;
            }

            return declarator.FirstAncestorOrSelf<SyntaxNode>(ancestor =>
                ancestor is
                    FieldDeclarationSyntax or
                    LocalDeclarationStatementSyntax);
        }

        return node.FirstAncestorOrSelf<SyntaxNode>(ancestor =>
            ancestor is
                PropertyDeclarationSyntax or
                ParameterSyntax or
                MethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                DelegateDeclarationSyntax);
    }

    static bool IsMethodHost(SyntaxNode? host) =>
        host is
            MethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            DelegateDeclarationSyntax;

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
        var argument = KnownSyntaxConstants.IsKnown(value)
            ? $"Syntax.{value}"
            : $"\"{value}\"";
        return host switch
        {
            LocalDeclarationStatementSyntax => (
                $"Add //language={ToRiderToken(value)} to {description}",
                $"AddLanguageComment:{value}"),
            _ when IsMethodHost(host) => (
                $"Add [ReturnSyntax({argument})] to {description}",
                $"AddReturnSyntax:{value}"),
            _ => (
                $"Add [Syntax({argument})] to {description}",
                $"AddSyntax:{value}")
        };
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
            IsMethodHost(host) ||
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

    static SyntaxNode? AddParameterlessAttribute(SyntaxNode host, string name)
    {
        var attribute = Attribute(IdentifierName(name));
        var attributes = AttributeList(SingletonSeparatedList(attribute))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return host switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(attributes),
            FieldDeclarationSyntax field => field.AddAttributeLists(attributes),
            ParameterSyntax parameter => parameter.AddAttributeLists(attributes),
            _ => null
        };
    }

    // Rider docs spell regex as `regexp`. Normalizing on write means the emitted
    // comment lights up Rider's own highlighting; MismatchAnalyzer's read path maps
    // `regexp` back to `Regex` so the round-trip matches the BCL constant.
    static string ToRiderToken(string value)
    {
        if (value.Equals("Regex", StringComparison.Ordinal))
        {
            return "regexp";
        }

        return value.ToLowerInvariant();
    }

    // Emits the Rider/IntelliJ-compatible `//language=<token>` comment above the
    // declaration. The value is lowercased to match the convention used in Rider's
    // own docs (e.g. `//language=regex`); MismatchAnalyzer's read path is
    // first-character-case-insensitive, so this round-trips cleanly against the BCL
    // PascalCase constants (`Regex`, `Json`, ...).
    static SyntaxNode AddLanguageCommentToLocal(LocalDeclarationStatementSyntax local, string value)
    {
        var comment = Comment($"// language={ToRiderToken(value)}");
        var eol = CarriageReturnLineFeed;

        var existingLeading = local.GetLeadingTrivia();
        var indent = FindCurrentLineIndent(existingLeading);

        var prefix = indent.IsKind(SyntaxKind.WhitespaceTrivia)
            ? TriviaList(indent, comment, eol)
            : TriviaList(comment, eol);

        return local.WithLeadingTrivia(prefix.AddRange(existingLeading));
    }

    // The current-line indent is the trailing whitespace trivia of the leading trivia
    // list — i.e. the last whitespace before the token. Earlier whitespace may belong
    // to blank-line gaps between this statement and the previous one.
    static SyntaxTrivia FindCurrentLineIndent(SyntaxTriviaList trivia)
    {
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var item = trivia[i];

            if (item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return item;
            }

            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                break;
            }
        }

        return default;
    }

    static (string Title, string EquivalenceKey) BuildUnionFixMetadata(SyntaxNode? host, string[] values)
    {
        var description = host is null ? "declaration" : HostDescription.Describe(host);
        var argumentList = string.Join(", ", values.Select(FormatArgument));
        var attributeName = IsMethodHost(host) ? "ReturnSyntax" : "UnionSyntax";
        return (
            $"Add [{attributeName}({argumentList})] to {description}",
            $"Add{attributeName}:{string.Join('|', values)}");
    }

    static string FormatArgument(string value) =>
        KnownSyntaxConstants.IsKnown(value) ? $"Syntax.{value}" : $"\"{value}\"";

    static SyntaxNode? AddUnionSyntaxAttribute(SyntaxNode host, string[] values)
    {
        var arguments = values.Select(value =>
        {
            ExpressionSyntax expression = KnownSyntaxConstants.IsKnown(value)
                ? MemberAccessExpression(
                    SyntaxKind.SimpleMemberAccessExpression,
                    IdentifierName("Syntax"),
                    IdentifierName(value))
                : LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
            return AttributeArgument(expression);
        });

        var attributeName = IsMethodHost(host) ? "ReturnSyntax" : "UnionSyntax";
        var attribute = Attribute(IdentifierName(attributeName))
            .WithArgumentList(AttributeArgumentList(SeparatedList(arguments)));

        var attributes = AttributeList(SingletonSeparatedList(attribute))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return host switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(attributes),
            FieldDeclarationSyntax field => field.AddAttributeLists(attributes),
            ParameterSyntax parameter => parameter.AddAttributeLists(attributes),
            MethodDeclarationSyntax method => method.AddAttributeLists(attributes),
            LocalFunctionStatementSyntax local => local.AddAttributeLists(attributes),
            DelegateDeclarationSyntax del => del.AddAttributeLists(attributes),
            _ => null
        };
    }

    static SyntaxNode? AddStringSyntaxAttribute(
        SyntaxNode host,
        string value,
        string attributeName,
        bool useConstant)
    {
        var expression = useConstant
            ? (ExpressionSyntax)MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("Syntax"),
                IdentifierName(value))
            : LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
        var argument = AttributeArgument(expression);

        var attribute = Attribute(IdentifierName(attributeName))
            .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)));

        var attributes = AttributeList(SingletonSeparatedList(attribute))
            .WithAdditionalAnnotations(Formatter.Annotation);

        return host switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(attributes),
            FieldDeclarationSyntax field => field.AddAttributeLists(attributes),
            ParameterSyntax parameter => parameter.AddAttributeLists(attributes),
            MethodDeclarationSyntax method => method.AddAttributeLists(attributes),
            LocalFunctionStatementSyntax local => local.AddAttributeLists(attributes),
            DelegateDeclarationSyntax del => del.AddAttributeLists(attributes),
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

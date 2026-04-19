// SSA007 codefix path: replace a redundant `[StringSyntax("Html")]` (or
// `[ReturnSyntax("Html")]`) with the bare `[Html]` shortcut attribute the
// generator emits when the consumer opts in. Self-contained — no overlap
// with the SSA002/003/005 add-attribute path.
static class ShortcutReplacer
{
    // Kept in sync with MismatchAnalyzer's DiagnosticDescriptor properties.
    const string valueKey = "StringSyntaxValue";

    public static async Task RegisterAsync(CodeFixContext context, Diagnostic diagnostic)
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
                cancel => ReplaceAsync(
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

    static async Task<Solution> ReplaceAsync(
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
        if (list?.Parent is MethodDeclarationSyntax or LocalFunctionStatementSyntax or DelegateDeclarationSyntax &&
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
}

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReplaceUnionWithStringSyntaxCodeFixProvider))]
[Shared]
public class ReplaceUnionWithStringSyntaxCodeFixProvider :
    CodeFixProvider
{
    const string valueKey = "StringSyntaxValue";
    const string singletonUnionId = "SSA006";

    public override ImmutableArray<string> FixableDiagnosticIds => [singletonUnionId];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        var root = await context.Document
            .GetSyntaxRootAsync(context.CancellationToken)
            .ConfigureAwait(false);

        if (root is null)
        {
            return;
        }

        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(valueKey, out var value) || value is null)
            {
                continue;
            }

            // Normalize to canonical PascalCase so a UnionSyntax option spelled
            // `"html"` resolves to `Syntax.Html` rather than degrading to `"html"`.
            if (KnownSyntaxConstants.TryGetCanonical(value, out var canonical))
            {
                value = canonical;
            }

            var attribute = root
                .FindNode(diagnostic.Location.SourceSpan)
                .FirstAncestorOrSelf<AttributeSyntax>();
            if (attribute is null)
            {
                continue;
            }

            var compilation = await context.Document.Project
                .GetCompilationAsync(context.CancellationToken)
                .ConfigureAwait(false);

            var attributeName = ResolveAttributeName(compilation);
            var useConstant = attributeName == "Syntax" && KnownSyntaxConstants.IsKnown(value);
            var argumentText = useConstant ? $"Syntax.{value}" : $"\"{value}\"";

            var title = HostDescription.FindAttributeOwner(attribute) is { } owner
                ? $"Replace [UnionSyntax] on {HostDescription.Describe(owner)} with [{attributeName}({argumentText})]"
                : $"Replace with [{attributeName}({argumentText})]";

            context.RegisterCodeFix(
                CodeAction.Create(
                    title,
                    cancel => ReplaceAttributeAsync(context.Document, attribute, value, attributeName, useConstant, cancel),
                    equivalenceKey: $"ReplaceUnionWithSyntax:{value}"),
                diagnostic);
        }
    }

    static async Task<Document> ReplaceAttributeAsync(
        Document document,
        AttributeSyntax oldAttribute,
        string value,
        string attributeName,
        bool useConstant,
        Cancel cancel)
    {
        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var expression = useConstant
            ? (ExpressionSyntax)MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("Syntax"),
                IdentifierName(value))
            : LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
        var argument = AttributeArgument(expression);
        var newAttribute = Attribute(IdentifierName(attributeName))
            .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)))
            .WithTriviaFrom(oldAttribute)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(oldAttribute, newAttribute);
        return document.WithSyntaxRoot(newRoot);
    }

    // Prefer `[Syntax(...)]` when the generator's `global using SyntaxAttribute = ...`
    // alias is in scope. Fall back to `[StringSyntax(...)]` otherwise.
    static string ResolveAttributeName(Compilation? compilation)
    {
        if (compilation is null)
        {
            return "StringSyntax";
        }

        foreach (var tree in compilation.SyntaxTrees)
        {
            var root = tree.GetRoot();
            foreach (var usingDirective in root
                         .DescendantNodes(_ =>
                             _ is CompilationUnitSyntax or
                                 NamespaceDeclarationSyntax or
                                 FileScopedNamespaceDeclarationSyntax)
                         .OfType<UsingDirectiveSyntax>())
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

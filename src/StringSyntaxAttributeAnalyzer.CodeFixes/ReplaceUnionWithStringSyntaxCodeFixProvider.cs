namespace StringSyntaxAttributeAnalyzer;

[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(ReplaceUnionWithStringSyntaxCodeFixProvider))]
[Shared]
public class ReplaceUnionWithStringSyntaxCodeFixProvider : CodeFixProvider
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

            var attribute = root
                .FindNode(diagnostic.Location.SourceSpan)
                .FirstAncestorOrSelf<AttributeSyntax>();
            if (attribute is null)
            {
                continue;
            }

            context.RegisterCodeFix(
                CodeAction.Create(
                    $"Replace with [StringSyntax(\"{value}\")]",
                    cancel => ReplaceAttributeAsync(context.Document, attribute, value, cancel),
                    equivalenceKey: $"ReplaceUnionWithStringSyntax:{value}"),
                diagnostic);
        }
    }

    static async Task<Document> ReplaceAttributeAsync(
        Document document,
        AttributeSyntax oldAttribute,
        string value,
        Cancel cancel)
    {
        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var argument = AttributeArgument(
            LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value)));
        var newAttribute = Attribute(IdentifierName("StringSyntax"))
            .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)))
            .WithTriviaFrom(oldAttribute)
            .WithAdditionalAnnotations(Formatter.Annotation);

        var newRoot = root.ReplaceNode(oldAttribute, newAttribute);
        return document.WithSyntaxRoot(newRoot);
    }
}

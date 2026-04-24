// Pure syntax-construction helpers for the codefix's "produce a new node" step.
// All methods take an existing host and return a replacement node with the new
// attribute (or language comment) attached. Workspace concerns — Solution,
// Document, Formatter pipeline — live in the provider.
static class AttributeNodeBuilder
{
    public static SyntaxNode? AddParameterless(SyntaxNode host, string name)
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

    public static SyntaxNode? AddStringSyntax(
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

    public static SyntaxNode? AddUnionSyntax(SyntaxNode host, string[] values)
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

        var attributeName = AttributeHost.IsMethod(host) ? "ReturnSyntax" : "UnionSyntax";
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

    // Emits the Rider/IntelliJ-compatible `//language=<token>` comment above the
    // declaration. The first character is lowered to match Rider's convention
    // (e.g. `//language=regex`) and to round-trip against the BCL PascalCase
    // constants (`Regex`, `Json`, ...) through MismatchAnalyzer's
    // first-character-case-insensitive compare.
    public static SyntaxNode AddLanguageCommentToLocal(LocalDeclarationStatementSyntax local, params string[] values)
    {
        var token = string.Join('|', values.Select(ToRiderToken));
        var comment = Comment($"// language={token}");
        var eol = CarriageReturnLineFeed;

        var existingLeading = local.GetLeadingTrivia();
        var indentIndex = FindCurrentLineIndentIndex(existingLeading);

        // Insert the comment immediately above the `var` line so any pre-existing
        // blank-line separator in `existingLeading` stays above the comment rather
        // than sliding between the comment and the local.
        if (indentIndex >= 0)
        {
            var indent = existingLeading[indentIndex];
            var newLeading = existingLeading
                .RemoveAt(indentIndex)
                .AddRange([indent, comment, eol, indent]);
            return local.WithLeadingTrivia(newLeading);
        }

        return local.WithLeadingTrivia(existingLeading.AddRange([comment, eol]));
    }

    public static string FormatArgument(string value) =>
        KnownSyntaxConstants.IsKnown(value) ? $"Syntax.{value}" : $"\"{value}\"";

    // Rider docs spell regex as `regexp`. Normalizing on write means the emitted
    // comment lights up Rider's own highlighting; MismatchAnalyzer's read path maps
    // `regexp` back to `Regex` so the round-trip matches the BCL constant.
    //
    // Only the first character is lowered. The analyzer's SingleValueMatches is
    // first-char-case-insensitive but ordinal beyond that, so lowercasing the whole
    // value would break round-trip for compound constants like `DateOnlyFormat`
    // (which would become `dateonlyformat` and no longer match `DateOnlyFormat`).
    public static string ToRiderToken(string value)
    {
        if (value.Equals("Regex", StringComparison.Ordinal))
        {
            return "regexp";
        }

        if (value.Length == 0 || !char.IsUpper(value[0]))
        {
            return value;
        }

        return char.ToLowerInvariant(value[0]) + value.Substring(1);
    }

    // The current-line indent is the trailing whitespace trivia of the leading trivia
    // list — i.e. the last whitespace before the token. Earlier whitespace may belong
    // to blank-line gaps between this statement and the previous one.
    static int FindCurrentLineIndentIndex(SyntaxTriviaList trivia)
    {
        for (var i = trivia.Count - 1; i >= 0; i--)
        {
            var item = trivia[i];

            if (item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                return i;
            }

            if (item.IsKind(SyntaxKind.EndOfLineTrivia))
            {
                break;
            }
        }

        return -1;
    }
}

// Pure syntax-construction helpers for the codefix's "produce a new node" step.
// All methods take an existing host and return a replacement node with the new
// attribute (or language comment) attached. Workspace concerns — Solution,
// Document, Formatter pipeline — live in the provider.
static class AttributeNodeBuilder
{
    // The namespace the generator emits UnionSyntaxAttribute, ReturnSyntaxAttribute and
    // the Syntax constants into. Only imported when the consumer keeps the generator's
    // global usings, so without them anything from here has to be written qualified.
    public const string GeneratedNamespace = "StringSyntaxAttributeAnalyzer";

    public static SyntaxNode? AddParameterless(SyntaxNode host, string name)
    {
        // Shortcut attributes are generated with an AttributeUsage of
        // Field | Parameter | Property | ReturnValue, so they can't go on the method and
        // local-function hosts Attach otherwise accepts.
        if (host is not (
            PropertyDeclarationSyntax or
            IndexerDeclarationSyntax or
            FieldDeclarationSyntax or
            ParameterSyntax))
        {
            return null;
        }

        var attribute = Attribute(IdentifierName(name));
        return Attach(host, AttributeList(SingletonSeparatedList(attribute)));
    }

    public static SyntaxNode? AddStringSyntax(
        SyntaxNode host,
        string value,
        string attributeName,
        bool useConstant)
    {
        var argument = AttributeArgument(ValueExpression(value, useConstant));
        var attribute = Attribute(ParseName(attributeName))
            .WithArgumentList(AttributeArgumentList(SingletonSeparatedList(argument)));

        return Attach(host, AttributeList(SingletonSeparatedList(attribute)));
    }

    public static SyntaxNode? AddUnionSyntax(
        SyntaxNode host,
        string[] values,
        string attributeName,
        bool useConstants)
    {
        var arguments = values.Select(_ =>
            AttributeArgument(ValueExpression(_, useConstants && KnownSyntaxConstants.IsKnown(_))));

        var attribute = Attribute(ParseName(attributeName))
            .WithArgumentList(AttributeArgumentList(SeparatedList(arguments)));

        return Attach(host, AttributeList(SingletonSeparatedList(attribute)));
    }

    static ExpressionSyntax ValueExpression(string value, bool useConstant)
    {
        if (useConstant)
        {
            return MemberAccessExpression(
                SyntaxKind.SimpleMemberAccessExpression,
                IdentifierName("Syntax"),
                IdentifierName(value));
        }

        return LiteralExpression(SyntaxKind.StringLiteralExpression, Literal(value));
    }

    // Attaches a new attribute list to a declaration, moving the declaration's leading
    // trivia in front of it.
    //
    // AddAttributeLists alone prepends the list but leaves that trivia on the token that
    // used to come first, which strands anything written above the member *between* the
    // new attribute and the modifiers: a doc comment there stops being documentation
    // (CS1587, and the member silently loses its docs), and a `#region` gets separated
    // from what it opens. The trivia is re-attached to the node rather than to the
    // attribute list so it lands on whichever token ends up first.
    static SyntaxNode? Attach(SyntaxNode host, AttributeListSyntax attributes)
    {
        attributes = attributes.WithAdditionalAnnotations(Formatter.Annotation);

        SyntaxNode? withAttribute = host.WithoutLeadingTrivia() switch
        {
            PropertyDeclarationSyntax property => property.AddAttributeLists(attributes),
            IndexerDeclarationSyntax indexer => indexer.AddAttributeLists(attributes),
            FieldDeclarationSyntax field => field.AddAttributeLists(attributes),
            ParameterSyntax parameter => parameter.AddAttributeLists(attributes),
            MethodDeclarationSyntax method => method.AddAttributeLists(attributes),
            LocalFunctionStatementSyntax local => local.AddAttributeLists(attributes),
            DelegateDeclarationSyntax declaration => declaration.AddAttributeLists(attributes),
            _ => null
        };

        return withAttribute?.WithLeadingTrivia(host.GetLeadingTrivia());
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

    public static string FormatArgument(string value)
    {
        if (KnownSyntaxConstants.IsKnown(value))
        {
            return $"Syntax.{value}";
        }

        return $"\"{value}\"";
    }

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

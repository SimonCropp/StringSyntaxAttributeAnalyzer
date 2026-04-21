// SSA008 codefix: the symbol's name already matches a known convention, so the
// `[StringSyntax(...)]` (or shortcut / single-value Return/Union) annotation —
// or the `//language=<token>` comment on a local — is redundant. Remove it.
//
// Two diagnostic flavours, distinguished by the `ConventionTarget` property the
// analyzer attaches:
//   * "Attribute"        — Location is the AttributeSyntax. Strip the
//                          attribute (or the whole AttributeListSyntax when it
//                          held only that one attribute).
//   * "LanguageComment"  — Location is the LocalDeclarationStatementSyntax.
//                          Find the first `language=` trivia (preceding-line
//                          single-line comment OR inline block comment) and
//                          remove it (plus its trailing EOL when it occupied
//                          its own line).
[ExportCodeFixProvider(LanguageNames.CSharp, Name = nameof(RemoveRedundantConventionCodeFixProvider))]
[Shared]
public class RemoveRedundantConventionCodeFixProvider : CodeFixProvider
{
    const string redundantByConventionId = "SSA008";
    const string targetKey = "ConventionTarget";

    public override ImmutableArray<string> FixableDiagnosticIds => [redundantByConventionId];

    public override FixAllProvider GetFixAllProvider() =>
        WellKnownFixAllProviders.BatchFixer;

    public override async Task RegisterCodeFixesAsync(CodeFixContext context)
    {
        foreach (var diagnostic in context.Diagnostics)
        {
            if (!diagnostic.Properties.TryGetValue(targetKey, out var target))
            {
                continue;
            }

            var root = await context.Document
                .GetSyntaxRootAsync(context.CancellationToken)
                .ConfigureAwait(false);
            if (root is null)
            {
                continue;
            }

            if (target == "Attribute")
            {
                var node = root.FindNode(diagnostic.Location.SourceSpan);
                var attribute = node as AttributeSyntax ?? node.FirstAncestorOrSelf<AttributeSyntax>();
                if (attribute is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove redundant StringSyntax annotation",
                        cancel => RemoveAttributeAsync(context.Document, attribute, cancel),
                        equivalenceKey: "RemoveRedundantStringSyntax"),
                    diagnostic);
            }
            else if (target == "LanguageComment")
            {
                var declaration = root
                    .FindNode(diagnostic.Location.SourceSpan)
                    .FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
                if (declaration is null)
                {
                    continue;
                }

                context.RegisterCodeFix(
                    CodeAction.Create(
                        "Remove redundant //language= comment",
                        cancel => RemoveLanguageCommentAsync(context.Document, declaration, cancel),
                        equivalenceKey: "RemoveRedundantLanguageComment"),
                    diagnostic);
            }
        }
    }

    static async Task<Document> RemoveAttributeAsync(
        Document document,
        AttributeSyntax attribute,
        Cancel cancel)
    {
        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        var list = attribute.FirstAncestorOrSelf<AttributeListSyntax>();

        SyntaxNode newRoot;
        if (list is { Attributes.Count: 1 })
        {
            // Removing the whole list — preserve the list's trailing trivia onto the
            // owning declaration so we don't leave a blank line where the attribute
            // was. RemoveNode with KeepNoTrivia handles the common case cleanly.
            newRoot = root.RemoveNode(list, SyntaxRemoveOptions.KeepNoTrivia)!;
        }
        else
        {
            newRoot = root.RemoveNode(attribute, SyntaxRemoveOptions.KeepNoTrivia)!;
        }

        return document.WithSyntaxRoot(newRoot);
    }

    static async Task<Document> RemoveLanguageCommentAsync(
        Document document,
        LocalDeclarationStatementSyntax declaration,
        Cancel cancel)
    {
        var root = await document.GetSyntaxRootAsync(cancel).ConfigureAwait(false);
        if (root is null)
        {
            return document;
        }

        // Two locations to check, mirroring the reader: the statement's leading
        // trivia (preceding-line comment) and any descendant trivia (inline block
        // comment). The leading-trivia case also strips the comment's trailing EOL
        // and any leading whitespace on the same line so we don't leave a hanging
        // blank line.
        var leading = declaration.GetLeadingTrivia();
        var rebuilt = TryRemoveFromLeadingTrivia(leading, out var newLeading);
        if (rebuilt)
        {
            var newDeclaration = declaration.WithLeadingTrivia(newLeading);
            return document.WithSyntaxRoot(root.ReplaceNode(declaration, newDeclaration));
        }

        // Inline form — `var x = /*language=X*/ "..."`. Find the trivia, replace
        // the containing token's trivia lists with the stripped version.
        foreach (var trivia in declaration.DescendantTrivia())
        {
            if (!IsLanguageTrivia(trivia))
            {
                continue;
            }

            var token = trivia.Token;
            var newToken = token
                .WithLeadingTrivia(StripLanguageTrivia(token.LeadingTrivia))
                .WithTrailingTrivia(StripLanguageTrivia(token.TrailingTrivia));
            return document.WithSyntaxRoot(root.ReplaceToken(token, newToken));
        }

        return document;
    }

    static bool TryRemoveFromLeadingTrivia(SyntaxTriviaList trivia, out SyntaxTriviaList result)
    {
        for (var i = 0; i < trivia.Count; i++)
        {
            if (!IsLanguageTrivia(trivia[i]))
            {
                continue;
            }

            // Strip:
            //   * any whitespace immediately before the comment on the same line,
            //   * the comment trivia itself,
            //   * one trailing EndOfLine if present.
            // This avoids a leftover blank line when `// language=X` lived on its
            // own line.
            var start = i;
            if (start > 0 &&
                trivia[start - 1].IsKind(SyntaxKind.WhitespaceTrivia))
            {
                start--;
            }

            var end = i;
            if (end + 1 < trivia.Count &&
                trivia[end + 1].IsKind(SyntaxKind.EndOfLineTrivia))
            {
                end++;
            }

            var builder = new List<SyntaxTrivia>(trivia.Count - (end - start + 1));
            for (var j = 0; j < trivia.Count; j++)
            {
                if (j >= start && j <= end)
                {
                    continue;
                }
                builder.Add(trivia[j]);
            }

            result = TriviaList(builder);
            return true;
        }

        result = trivia;
        return false;
    }

    static SyntaxTriviaList StripLanguageTrivia(SyntaxTriviaList trivia)
    {
        var builder = new List<SyntaxTrivia>(trivia.Count);
        var skipNextWhitespace = false;
        foreach (var item in trivia)
        {
            if (IsLanguageTrivia(item))
            {
                skipNextWhitespace = true;
                continue;
            }

            if (skipNextWhitespace &&
                item.IsKind(SyntaxKind.WhitespaceTrivia))
            {
                skipNextWhitespace = false;
                continue;
            }

            skipNextWhitespace = false;
            builder.Add(item);
        }

        return TriviaList(builder);
    }

    static bool IsLanguageTrivia(SyntaxTrivia trivia)
    {
        if (!trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) &&
            !trivia.IsKind(SyntaxKind.MultiLineCommentTrivia))
        {
            return false;
        }

        return ContainsLanguageDirective(trivia.ToString());
    }

    // Matches the `language\s*=\s*<identifier>` shape with `language` at a word
    // boundary — mirrors LanguageCommentReader.TryParse on the analyzer side.
    // Substring-only matching on "language" was stripping unrelated comments
    // that merely mentioned the word (e.g. "// notes about our query language").
    // Duplicated rather than shared so the codefix project stays decoupled from
    // the analyzer project — same policy as the duplicated diagnostic id and
    // property key strings.
    static bool ContainsLanguageDirective(string text)
    {
        const string keyword = "language";
        for (var i = 0; i <= text.Length - keyword.Length; i++)
        {
            if (!MatchesKeyword(text, i, keyword))
            {
                continue;
            }

            if (i > 0 && IsWordChar(text[i - 1]))
            {
                continue;
            }

            var pos = SkipWhitespace(text, i + keyword.Length);
            if (pos >= text.Length || text[pos] != '=')
            {
                continue;
            }

            pos = SkipWhitespace(text, pos + 1);
            var start = pos;
            while (pos < text.Length && IsWordChar(text[pos]))
            {
                pos++;
            }

            if (pos > start)
            {
                return true;
            }
        }

        return false;
    }

    static bool MatchesKeyword(string text, int index, string keyword)
    {
        for (var j = 0; j < keyword.Length; j++)
        {
            if (char.ToLowerInvariant(text[index + j]) != keyword[j])
            {
                return false;
            }
        }

        return true;
    }

    static int SkipWhitespace(string text, int pos)
    {
        while (pos < text.Length && char.IsWhiteSpace(text[pos]))
        {
            pos++;
        }

        return pos;
    }

    static bool IsWordChar(char c) =>
        char.IsLetterOrDigit(c) || c == '_';
}

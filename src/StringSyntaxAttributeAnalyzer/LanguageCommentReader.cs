// Reads JetBrains/IntelliJ-compatible language-injection comments
// (`//language=<name>` or `/*language=<name>*/`) from local-variable declarations.
// Optional `prefix=`/`postfix=` follow-on options are ignored — they're renderer
// hints, irrelevant to syntax identity. Doc:
// https://www.jetbrains.com/help/rider/Language_Injections.html
static class LanguageCommentReader
{
    const string keyword = "language";

    public static bool TryRead(ILocalSymbol local, out string syntax)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            var node = reference.GetSyntax();
            var statement = node.FirstAncestorOrSelf<LocalDeclarationStatementSyntax>();
            if (statement is null)
            {
                continue;
            }

            // Preceding-line form — `// language=regex` on the line above the statement.
            // Lives in the statement's own leading trivia.
            if (TryMatch(statement.GetLeadingTrivia(), out syntax))
            {
                return true;
            }

            // Inline form — `var x = /*language=regex*/ "..."`. Roslyn attaches the
            // block comment to the `=` token's trailing trivia (same-line trivia before
            // a token sticks to the previous token in Roslyn's default policy), so a
            // targeted check of the initializer's leading trivia misses it. Scan every
            // trivia within the statement and take the first match.
            foreach (var trivia in statement.DescendantTrivia())
            {
                if (!IsCommentTrivia(trivia))
                {
                    continue;
                }

                if (TryParse(trivia.ToString(), out syntax))
                {
                    return true;
                }
            }
        }

        syntax = "";
        return false;
    }

    static bool TryMatch(SyntaxTriviaList trivia, out string syntax)
    {
        foreach (var item in trivia)
        {
            if (!IsCommentTrivia(item))
            {
                continue;
            }

            if (TryParse(item.ToString(), out syntax))
            {
                return true;
            }
        }

        syntax = "";
        return false;
    }

    static bool IsCommentTrivia(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

    // Finds the first `language=<identifier>` occurrence where `language` sits at a
    // word boundary (start-of-string or preceded by a non-word char). Emulates the
    // previous regex `\blanguage\s*=\s*([A-Za-z0-9_]+)` without the regex engine —
    // comments are short, so linear scanning is cheap and avoids the Regex cache.
    static bool TryParse(string text, out string syntax)
    {
        for (var i = 0; i <= text.Length - keyword.Length; i++)
        {
            if (!MatchesKeyword(text, i))
            {
                continue;
            }

            // Word boundary before: start of string, or previous char is non-word.
            if (i > 0 && IsWordChar(text[i - 1]))
            {
                continue;
            }

            var pos = i + keyword.Length;
            pos = SkipWhitespace(text, pos);
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

            if (pos == start)
            {
                continue;
            }

            syntax = Normalize(text.Substring(start, pos - start));
            return true;
        }

        syntax = "";
        return false;
    }

    static bool MatchesKeyword(string text, int index)
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

    // Rider doc examples spell regex as `regexp`; the BCL constant is `Regex`. Bridge
    // the two so `//language=regexp` matches `[StringSyntax(StringSyntaxAttribute.Regex)]`
    // without the user having to know the naming history.
    static string Normalize(string raw) =>
        raw.Equals("regexp", StringComparison.OrdinalIgnoreCase) ? "Regex" : raw;
}

// Reads JetBrains/IntelliJ-compatible language-injection comments
// (`//language=<name>` or `/*language=<name>*/`) from local-variable declarations.
// Optional `prefix=`/`postfix=` follow-on options are ignored — they're renderer
// hints, irrelevant to syntax identity. Doc:
// https://www.jetbrains.com/help/rider/Language_Injections.html
static class LanguageCommentReader
{
    // Keyword `language` is matched case-insensitively; the captured value preserves
    // its case so PascalCase BCL constants like `Regex` round-trip cleanly.
    static readonly Regex pattern = new(
        @"\blanguage\s*=\s*([A-Za-z0-9_]+)",
        RegexOptions.IgnoreCase | RegexOptions.Compiled);

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

                var match = pattern.Match(trivia.ToString());
                if (match.Success)
                {
                    syntax = Normalize(match.Groups[1].Value);
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

            var match = pattern.Match(item.ToString());
            if (match.Success)
            {
                syntax = Normalize(match.Groups[1].Value);
                return true;
            }
        }

        syntax = "";
        return false;
    }

    static bool IsCommentTrivia(SyntaxTrivia trivia) =>
        trivia.IsKind(SyntaxKind.SingleLineCommentTrivia) ||
        trivia.IsKind(SyntaxKind.MultiLineCommentTrivia);

    // Rider doc examples spell regex as `regexp`; the BCL constant is `Regex`. Bridge
    // the two so `//language=regexp` matches `[StringSyntax(StringSyntaxAttribute.Regex)]`
    // without the user having to know the naming history.
    static string Normalize(string raw) =>
        raw.Equals("regexp", StringComparison.OrdinalIgnoreCase) ? "Regex" : raw;
}

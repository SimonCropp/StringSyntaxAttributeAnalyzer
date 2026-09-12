using System.Diagnostics.CodeAnalysis;

// Opt-in name-based conventions: when enabled, a member or local whose name
// matches a known convention (e.g. `url`, `pageHtml`) is treated as if it
// already carries the corresponding `[StringSyntax]` value. Lets the analyzer
// flag mismatches and redundant attributes purely from naming.
//
// Matching rules (per matcher token, all lowercase):
//   * case-insensitive equality:           `url`, `URL`, `Url` -> match
//   * PascalCase suffix (ordinal):         `pageHtml`, `myUrl` -> match
// The PascalCase suffix variant is what avoids matching `myhtml` (no word
// boundary) while still catching the common camelCase forms.
//
// The convention list is intentionally narrow — only values whose name tokens
// are distinctive enough that a false positive is unlikely. Format-style
// constants (`DateTimeFormat`, `NumericFormat`, ...) and the generic `Text`
// are deliberately omitted: their natural variable names (`format`, `text`)
// are too broad to safely promote.
static class NameConventions
{
    static readonly (string Value, string[] Matchers)[] conventions =
    [
        ("Uri", ["uri", "url"]),
        ("Html", ["html"]),
        ("Json", ["json"]),
        ("Xml", ["xml"]),
        ("Regex", ["regex"]),
        ("Sql", ["sql"]),
        ("Csv", ["csv"]),
        ("Yaml", ["yaml"]),
        ("Markdown", ["markdown"]),
        ("Email", ["email"])
    ];

    // Symbol-aware entry point. Every check that has a symbol in hand should come
    // through here rather than reaching for `.Name`, so the underscore rule below is
    // applied uniformly — a field that is Present-by-name on one path and bare on
    // another produces contradictory diagnostics.
    public static bool TryMatch(ISymbol symbol, [NotNullWhen(true)] out string? value) =>
        TryMatch(ConventionName(symbol), out value);

    // A field's leading underscores are punctuation, not part of its name, so they are
    // stripped before matching: `_html` reads as `html`, `_pageHtml` as `pageHtml`.
    // What follows the prefix is already camelCase by the same convention, which is
    // exactly what the matchers below expect, so no re-casing is needed.
    //
    // Field-only. A leading underscore has no established meaning on a property, and on
    // a parameter it marks a discard. A field named with nothing but underscores has no
    // name left to match and gets no value.
    public static string ConventionName(ISymbol symbol)
    {
        var name = symbol.Name;
        if (symbol is not IFieldSymbol)
        {
            return name;
        }

        var start = 0;
        while (start < name.Length &&
               name[start] == '_')
        {
            start++;
        }

        return start == 0 ? name : name[start..];
    }

    public static bool TryMatch(string? name, [NotNullWhen(true)] out string? value)
    {
        if (name is { Length: > 0 })
        {
            foreach (var (conventionValue, matchers) in conventions)
            {
                foreach (var matcher in matchers)
                {
                    if (Matches(name, matcher))
                    {
                        value = conventionValue;
                        return true;
                    }
                }
            }
        }

        value = null;
        return false;
    }

    static bool Matches(string name, string matcher)
    {
        if (name.Length == matcher.Length)
        {
            return string.Equals(name, matcher, StringComparison.OrdinalIgnoreCase);
        }

        // PascalCase suffix: `pageHtml` ends with `Html`. Requires the
        // character at `name[^matcher.Length]` to be uppercase (so `myhtml`
        // does NOT match — no word boundary).
        if (name.Length <= matcher.Length)
        {
            return false;
        }

        var suffixStart = name.Length - matcher.Length;
        if (!char.IsUpper(name[suffixStart]))
        {
            return false;
        }

        if (char.ToLowerInvariant(name[suffixStart]) != matcher[0])
        {
            return false;
        }

        for (var i = 1; i < matcher.Length; i++)
        {
            if (name[suffixStart + i] != matcher[i])
            {
                return false;
            }
        }

        return true;
    }
}

// Names of the constants emitted on the `Syntax` class by SyntaxConstantsGenerator.
// Duplicated (rather than shared via ProjectReference) to keep the codefix project
// decoupled from the analyzer project — same reasoning as the duplicated diagnostic
// id strings. When a new constant is added to the generator, add it here too.
static class KnownSyntaxConstants
{
    // Exposed (internal) so tests can cross-check against the constants the
    // generator actually emits — catches the case where a new constant is added
    // to SyntaxConstantsGenerator but forgotten here, which would silently
    // degrade the codefix to emitting a string literal.
    internal static readonly ImmutableHashSet<string> Names =
    [
        "CompositeFormat",
        "DateOnlyFormat",
        "DateTimeFormat",
        "EnumFormat",
        "GuidFormat",
        "Json",
        "NumericFormat",
        "Regex",
        "TimeOnlyFormat",
        "TimeSpanFormat",
        "Uri",
        "Xml",
        "Html",
        "Text",
        "Email",
        "Markdown",
        "Yaml",
        "Csv",
        "Sql"
    ];

    // Lookup keyed by `lower(first-char) + rest`. Mirrors SyntaxValueMatcher's
    // first-character-case-insensitive comparison so values written as `"html"` (e.g.
    // from a Rider-style `// language=html` convention) resolve to the canonical
    // `Html` and pick up the shortcut attribute / `Syntax.Html` constant the same
    // way `"Html"` would.
    static readonly ImmutableDictionary<string, string> canonicalByFoldedKey =
        Names.ToImmutableDictionary(FoldKey, name => name);

    static string FoldKey(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    public static bool TryGetCanonical(string? value, out string canonical)
    {
        if (string.IsNullOrEmpty(value))
        {
            canonical = value!;
            return false;
        }

        if (canonicalByFoldedKey.TryGetValue(FoldKey(value!), out var resolved))
        {
            canonical = resolved;
            return true;
        }

        canonical = value!;
        return false;
    }

    public static bool IsKnown(string? value) => TryGetCanonical(value, out _);
}

// Value-set comparison for `[StringSyntax]` / `[UnionSyntax]` / `[ReturnSyntax]`
// identifiers. Two sets "match" if they overlap on at least one value —
// `[StringSyntax(x)]` ↔ `[UnionSyntax(x, y)]` matches via `x`, union ↔ union passes
// when the intersection is non-empty.
static class SyntaxValueMatcher
{
    // The wildcard syntax value. `[StringSyntax("*")]` (or any annotation whose
    // value set includes "*") marks a slot as accepting a value of ANY syntax —
    // used by passthrough/serialization APIs (snapshot testers, loggers) that
    // don't care which syntax flows through. Mapped to SyntaxState.Any at info-
    // construction time so it never reaches value comparison as a literal.
    public const string AnySentinel = "*";

    public static bool IsAnySentinel(string? value) => value == AnySentinel;

    public static bool ContainsAnySentinel(ImmutableArray<string> values)
    {
        if (values.IsDefaultOrEmpty)
        {
            return false;
        }

        foreach (var value in values)
        {
            if (IsAnySentinel(value))
            {
                return true;
            }
        }

        return false;
    }

    // First character is compared case-insensitively so `"json"` and `"Json"` are
    // equivalent; the BCL constants (`Regex`, `Json`, `Xml`, `DateTimeFormat`, …) are
    // PascalCase and we want lowercase variants (Rider-style `//language=json`) to
    // round-trip. Beyond the first character the match is ordinal — `"jSon"` vs
    // `"json"` is still a mismatch.
    public static bool ValuesMatch(ImmutableArray<string> a, ImmutableArray<string> b)
    {
        foreach (var va in a)
        {
            foreach (var vb in b)
            {
                if (SingleValueMatches(va, vb))
                {
                    return true;
                }
            }
        }
        return false;
    }

    public static string FormatValues(ImmutableArray<string> values) =>
        values.IsDefaultOrEmpty ? "" : string.Join('|', values);

    public static bool SingleValuesMatch(string? a, string? b) => SingleValueMatches(a, b);

    static bool SingleValueMatches(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        if (a.Length == 0)
        {
            return true;
        }

        if (char.ToLowerInvariant(a[0]) != char.ToLowerInvariant(b[0]))
        {
            return false;
        }

        return string.CompareOrdinal(a, 1, b, 1, a.Length - 1) == 0;
    }
}

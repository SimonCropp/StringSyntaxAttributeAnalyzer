// Value-set comparison for `[StringSyntax]` / `[UnionSyntax]` / `[ReturnSyntax]`
// identifiers. Two sets "match" if they overlap on at least one value —
// `[StringSyntax(x)]` ↔ `[UnionSyntax(x, y)]` matches via `x`, union ↔ union passes
// when the intersection is non-empty.
static class SyntaxValueMatcher
{
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
        values.IsDefaultOrEmpty ? "" : string.Join("|", values);

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

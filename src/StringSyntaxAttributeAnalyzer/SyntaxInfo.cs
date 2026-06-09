readonly struct SyntaxInfo(SyntaxState state, ImmutableArray<string> values)
{
    public SyntaxState State { get; } = state;

    // The set of accepted syntax values. Single-valued for `[StringSyntax(x)]`,
    // multi-valued for `[UnionSyntax(a, b, c)]`. Empty when State != Present.
    public ImmutableArray<string> Values { get; } = values;

    // Primary value to surface in messages and codefixes. For a union, takes the
    // first option — consistent but arbitrary; consumers overriding to a specific
    // value is expected.
    public string? PrimaryValue => Values.IsDefaultOrEmpty ? null : Values[0];

    public static SyntaxInfo Unknown { get; } = new(SyntaxState.Unknown, []);
    public static SyntaxInfo NotPresent { get; } = new(SyntaxState.NotPresent, []);

    // The `[StringSyntax("*")]` wildcard. Carries no values — Any never participates
    // in value matching; it short-circuits to "no diagnostic" wherever it appears.
    public static SyntaxInfo Any { get; } = new(SyntaxState.Any, []);

    public static SyntaxInfo Present(string value) =>
        SyntaxValueMatcher.IsAnySentinel(value) ? Any : new(SyntaxState.Present, [value]);
    public static SyntaxInfo PresentUnion(ImmutableArray<string> values) =>
        SyntaxValueMatcher.ContainsAnySentinel(values) ? Any : new(SyntaxState.Present, values);
}
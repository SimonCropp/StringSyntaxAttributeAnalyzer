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
    public static SyntaxInfo Present(string value) => new(SyntaxState.Present, [value]);
    public static SyntaxInfo PresentUnion(ImmutableArray<string> values) =>
        new(SyntaxState.Present, values);
}
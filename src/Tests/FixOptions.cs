// Knobs for CodeFixVerify. Defaults describe the ordinary consumer: the generator's
// global usings in scope, shortcut attributes not opted in, name conventions off, and
// a source that produces exactly one diagnostic offering exactly one fix.
sealed class FixOptions
{
    // Which analyzer diagnostic to fix. Null requires the source to produce exactly one.
    public string? DiagnosticId { get; init; }

    // Which registered action to apply. Non-zero for diagnostics that offer one action
    // per option of a union.
    public int ActionIndex { get; init; }

    public bool EmitGlobalUsings { get; init; } = true;

    public bool EmitShortcutAttributes { get; init; }

    // Replaces the generated support documents outright, for tests that pin behaviour
    // against a specific set of usings.
    public string? SupportSource { get; init; }

    // `.editorconfig` values handed to the analyzer, e.g. `stringsyntax.name_conventions`.
    public Dictionary<string, string>? ConfigOptions { get; init; }
}

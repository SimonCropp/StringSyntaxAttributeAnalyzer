// EditorConfig knob: `stringsyntax.name_conventions = enabled` (default
// disabled) opts the consumer into name-based convention matching. When
// enabled:
//   * a member/local whose name matches a known convention (NameConventions)
//     is treated as Present(value) even without a `[StringSyntax]` attribute,
//   * `KnownUnannotatedAssemblies` no longer suppresses diagnostics on those
//     symbols — the convention can speak for them,
//   * SSA008 fires when a symbol carries an attribute that the name
//     convention already implies.
//
// Per-tree resolution mirrors NamespaceSuppression.
sealed class NameConventionsOption(AnalyzerOptions options)
{
    const string optionsKey = "stringsyntax.name_conventions";

    bool globalEnabled = Parse(
        options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(optionsKey, out var configured)
            ? configured
            : null);

    ConcurrentDictionary<SyntaxTree, bool> perTreeCache = new();

    public bool IsEnabled(SyntaxTree? tree)
    {
        if (tree is null)
        {
            return globalEnabled;
        }

        return perTreeCache.GetOrAdd(
            tree,
            _ =>
            {
                var fileOptions = options.AnalyzerConfigOptionsProvider.GetOptions(_);
                if (fileOptions.TryGetValue(optionsKey, out var configured))
                {
                    return Parse(configured);
                }

                return globalEnabled;
            });
    }

    static bool Parse(string? raw) =>
        raw is not null &&
        (raw.Equals("enabled", StringComparison.OrdinalIgnoreCase) ||
         raw.Equals("true", StringComparison.OrdinalIgnoreCase));
}

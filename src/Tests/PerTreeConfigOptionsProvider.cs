// Mimics a real `[*.cs]` editorconfig section: options are visible via
// GetOptions(tree) but NOT via GlobalOptions. Used to verify the analyzer
// resolves per-tree options (so consumers can configure via `.editorconfig`
// without needing `.globalconfig` / `is_global = true`).
sealed class PerTreeConfigOptionsProvider(Dictionary<string, string> perFile) :
    AnalyzerConfigOptionsProvider
{
    static readonly AnalyzerConfigOptions empty = new TestConfigOptions([]);

    public override AnalyzerConfigOptions GlobalOptions => empty;

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) =>
        new TestConfigOptions(perFile);

    public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) =>
        empty;
}

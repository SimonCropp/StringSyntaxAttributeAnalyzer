sealed class TestConfigOptionsProvider(Dictionary<string, string> globals) :
    AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } = new TestConfigOptions(globals);
    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;
    public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) => GlobalOptions;
}
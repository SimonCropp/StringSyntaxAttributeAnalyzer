sealed class OptOutOptionsProvider(string emitGlobalUsingsValue) : AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new OptOutOptions(emitGlobalUsingsValue);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) => GlobalOptions;
}
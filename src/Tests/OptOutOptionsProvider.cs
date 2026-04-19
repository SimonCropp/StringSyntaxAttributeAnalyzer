sealed class OptOutOptionsProvider(
    string? emitGlobalUsingsValue = null,
    string? emitShortcutAttributesValue = null) :
    AnalyzerConfigOptionsProvider
{
    public override AnalyzerConfigOptions GlobalOptions { get; } =
        new OptOutOptions(emitGlobalUsingsValue, emitShortcutAttributesValue);

    public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

    public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) => GlobalOptions;
}

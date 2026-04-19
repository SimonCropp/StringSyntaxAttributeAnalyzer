sealed class OptOutOptions(
    string? emitGlobalUsingsValue = null,
    string? emitShortcutAttributesValue = null) :
    AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        if (key == "build_property.StringSyntaxAnalyzer_EmitGlobalUsings" &&
            emitGlobalUsingsValue is not null)
        {
            value = emitGlobalUsingsValue;
            return true;
        }

        if (key == "build_property.StringSyntaxAnalyzer_EmitShortcutAttributes" &&
            emitShortcutAttributesValue is not null)
        {
            value = emitShortcutAttributesValue;
            return true;
        }

        value = null;
        return false;
    }
}

sealed class OptOutOptions(string emitGlobalUsingsValue) : AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
    {
        if (key == "build_property.StringSyntaxAnalyzer_EmitGlobalUsings")
        {
            value = emitGlobalUsingsValue;
            return true;
        }

        value = null;
        return false;
    }
}
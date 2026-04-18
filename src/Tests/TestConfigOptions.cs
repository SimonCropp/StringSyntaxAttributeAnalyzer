sealed class TestConfigOptions(Dictionary<string, string> data) :
    AnalyzerConfigOptions
{
    public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value) =>
        data.TryGetValue(key, out value);
}
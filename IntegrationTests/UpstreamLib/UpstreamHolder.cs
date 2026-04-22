namespace UpstreamLib;

// Uses the generator-emitted Syntax class and attributes so the upstream
// assembly really carries the internal StringSyntaxAttributeAnalyzer types.
public class UpstreamHolder
{
    [StringSyntax(Syntax.Regex)]
    public string Pattern { get; set; } = "";

    [UnionSyntax(Syntax.Json, Syntax.Xml)]
    public string Payload { get; set; } = "";
}

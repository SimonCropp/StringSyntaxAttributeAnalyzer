using UpstreamLib;

// Deliberately references the generator-emitted types by simple name so the
// global using from the analyzer brings them into scope. If the generator
// also re-emits the types locally, these unqualified references resolve
// ambiguously between UpstreamLib's copy (visible via IVT) and this
// assembly's copy, producing CS0436 warnings.
public class DownstreamSample
{
    [StringSyntax(Syntax.Regex)]
    public string Pattern { get; set; } = "";

    [UnionSyntax(Syntax.Json, Syntax.Xml)]
    public string Payload { get; set; } = "";

    public void Consume()
    {
        var upstream = new UpstreamHolder();
        Pattern = upstream.Pattern;
    }
}

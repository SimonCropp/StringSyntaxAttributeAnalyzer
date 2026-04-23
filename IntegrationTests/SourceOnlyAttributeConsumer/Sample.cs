// Forces the generated Syntax class to be referenced from user code, so any
// CS0229 inside Syntax.Types.g.cs surfaces as a build failure.
using System.Diagnostics.CodeAnalysis;
using StringSyntaxAttributeAnalyzer;

internal class Sample
{
    [StringSyntax(Syntax.Regex)]
    public string? Pattern { get; set; }
}

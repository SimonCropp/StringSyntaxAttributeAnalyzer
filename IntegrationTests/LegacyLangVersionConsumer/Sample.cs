// Exercises every generated type from a C# 7.3 compilation: the StringSyntaxAttribute
// polyfill (netstandard2.0 has no BCL copy), the Syntax constants, UnionSyntaxAttribute
// and ReturnSyntaxAttribute. Referencing them from user code is what forces the
// generated files to be compiled, so a construct this language version does not support
// fails the build rather than sitting unnoticed in an unreferenced file.
//
// The usings are explicit because `global using` is C# 10 — the generator skips
// Syntax.Globals.g.cs below that, so nothing is in scope automatically here.
using System.Diagnostics.CodeAnalysis;
using StringSyntaxAttributeAnalyzer;

public class LegacySample
{
    [StringSyntax(Syntax.Regex)]
    public string Pattern { get; set; }

    [UnionSyntax(Syntax.Json, Syntax.Xml)]
    public string Payload { get; set; }

    [ReturnSyntax(Syntax.Json)]
    public string GetJson()
    {
        return "{}";
    }

    public static void ConsumeRegex([StringSyntax(Syntax.Regex)] string value)
    {
    }

    public void AssignMatching()
    {
        ConsumeRegex(Pattern);
    }
}

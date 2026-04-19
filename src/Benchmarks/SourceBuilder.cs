using System.Text;

namespace StringSyntaxAttributeAnalyzer.Benchmarks;

// Builds a synthetic compilation exercising every code path the analyzer
// registers: argument, assignment, property/field initializer, and binary
// equality — a realistic mix of Present/NotPresent/Unknown sources.
static class SourceBuilder
{
    public static string Build(int callSites)
    {
        var builder = new StringBuilder();
        builder.AppendLine("using System.Diagnostics.CodeAnalysis;");
        builder.AppendLine("public class Target");
        builder.AppendLine("{");
        builder.AppendLine("    [StringSyntax(StringSyntaxAttribute.Regex)]");
        builder.AppendLine("    public string Pattern { get; set; } = \"\";");
        builder.AppendLine("    public void ConsumeRegex([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }");
        builder.AppendLine("}");
        builder.AppendLine("public class Holder");
        builder.AppendLine("{");
        builder.AppendLine("    [StringSyntax(StringSyntaxAttribute.Regex)]");
        builder.AppendLine("    public string RegexValue { get; set; } = \"\";");
        builder.AppendLine("    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]");
        builder.AppendLine("    public string DateValue { get; set; } = \"\";");
        builder.AppendLine("    public string Untyped { get; set; } = \"\";");
        builder.AppendLine("}");
        builder.AppendLine("public class CallSites");
        builder.AppendLine("{");
        builder.AppendLine("    public void Run(Target target, Holder holder)");
        builder.AppendLine("    {");
        for (var i = 0; i < callSites; i++)
        {
            // Rotate through four shapes so the analyzer exercises each OperationKind.
            switch (i % 4)
            {
                case 0:
                    builder.AppendLine($"        target.ConsumeRegex(holder.RegexValue); // match, i={i}");
                    break;
                case 1:
                    builder.AppendLine($"        target.Pattern = holder.DateValue;     // SSA001, i={i}");
                    break;
                case 2:
                    builder.AppendLine($"        target.ConsumeRegex(holder.Untyped);   // SSA002, i={i}");
                    break;
                default:
                    builder.AppendLine($"        _ = holder.RegexValue == holder.DateValue; // SSA004, i={i}");
                    break;
            }
        }
        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }
}

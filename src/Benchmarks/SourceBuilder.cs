// Builds a synthetic compilation exercising every code path the analyzer
// registers: argument, assignment, property/field initializer, and binary
// equality — a realistic mix of Present/NotPresent/Unknown sources.
static class SourceBuilder
{
    public static string Build(int callSites)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            using System.Diagnostics.CodeAnalysis;
            public class Target
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; } = "";
                public void ConsumeRegex([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string RegexValue { get; set; } = "";
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string DateValue { get; set; } = "";
                public string Untyped { get; set; } = "";
            }
            public class CallSites
            {
                public void Run(Target target, Holder holder)
                {
            """);
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

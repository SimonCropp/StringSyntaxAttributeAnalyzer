namespace StringSyntaxAttributeAnalyzer.Benchmarks;

// Exercises the analyzer over source that flows BCL symbols into other BCL
// parameters — a stand-in for "real-world code that calls into annotated and
// unannotated assemblies". The branch-vs-main delta isolates the cost of the
// KnownUnannotatedAssemblies deny-list check on the suppression hot path.
[MemoryDiagnoser]
public class KnownUnannotatedAssembliesBenchmarks
{
    [Params(50, 250)]
    public int CallSites;

    CSharpCompilation compilation = null!;
    ImmutableArray<DiagnosticAnalyzer> analyzers;

    [GlobalSetup]
    public void Setup()
    {
        var tree = CSharpSyntaxTree.ParseText(BuildSource(CallSites));
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(_ => _.Length > 0)
            .Select(_ => (MetadataReference)MetadataReference.CreateFromFile(_))
            .ToArray();

        compilation = CSharpCompilation.Create(
            "KnownUnannotatedAssembliesBench",
            [tree],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        analyzers = [new MismatchAnalyzer()];
    }

    [Benchmark(Baseline = true)]
    public int CompileOnly() =>
        compilation.GetDiagnostics().Length;

    [Benchmark]
    public int Analyze() =>
        compilation
            .WithAnalyzers(analyzers)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult()
            .Length;

    static string BuildSource(int callSites)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            using System;
            using System.Text.RegularExpressions;
            using System.Text.Json;

            public class CallSites
            {
                public void Run(string input, DateTime when, Match match)
                {
            """);
        for (var i = 0; i < callSites; i++)
        {
            switch (i % 4)
            {
                case 0:
                    builder.AppendLine($"        Regex.IsMatch(input, when.ToString(\"yyyy\"));      // i={i}");
                    break;
                case 1:
                    builder.AppendLine($"        _ = DateTime.Parse(match.Value);                    // i={i}");
                    break;
                case 2:
                    builder.AppendLine($"        _ = Regex.IsMatch(\"abc\", \"pattern_{i}\");          // i={i}");
                    break;
                default:
                    builder.AppendLine($"        _ = JsonDocument.Parse(input);                       // i={i}");
                    break;
            }
        }

        builder.AppendLine("    }");
        builder.AppendLine("}");
        return builder.ToString();
    }
}

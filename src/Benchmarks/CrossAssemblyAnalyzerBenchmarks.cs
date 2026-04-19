namespace StringSyntaxAttributeAnalyzer.Benchmarks;

// Mirrors AnalyzerBenchmarks but places Target/Holder in a separate referenced
// assembly so attribute lookups flow through metadata symbols rather than
// source symbols. Compare against AnalyzerBenchmarks to see the marginal cost
// of crossing the assembly boundary — this is the path an [assembly: Index]
// attribute would be meant to optimize.
[MemoryDiagnoser]
public class CrossAssemblyAnalyzerBenchmarks
{
    [Params(10, 100, 500)]
    public int CallSites;

    CSharpCompilation compilation = null!;
    ImmutableArray<DiagnosticAnalyzer> analyzers;

    [GlobalSetup]
    public void Setup()
    {
        var tpaReferences = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(_ => _.Length > 0)
            .Select(_ => (MetadataReference)MetadataReference.CreateFromFile(_))
            .ToArray();

        var libraryTree = CSharpSyntaxTree.ParseText(
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
            """);

        var libraryCompilation = CSharpCompilation.Create(
            "BenchLib",
            [libraryTree],
            tpaReferences,
            new(OutputKind.DynamicallyLinkedLibrary));

        // Emit to a byte[] and reference via CreateFromImage so the consumer
        // compilation sees Target/Holder as metadata symbols, not source symbols.
        using var stream = new MemoryStream();
        var emit = libraryCompilation.Emit(stream);
        if (!emit.Success)
        {
            throw new InvalidOperationException(
                "Library compilation failed: " +
                string.Join(Environment.NewLine, emit.Diagnostics));
        }

        var libraryReference = MetadataReference.CreateFromImage(stream.ToArray());

        var consumerTree = CSharpSyntaxTree.ParseText(BuildConsumer(CallSites));

        compilation = CSharpCompilation.Create(
            "BenchConsumer",
            [consumerTree],
            [.. tpaReferences, libraryReference],
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

    static string BuildConsumer(int callSites)
    {
        var builder = new StringBuilder();
        builder.AppendLine(
            """
            public class CallSites
            {
                public void Run(Target target, Holder holder)
                {
            """);
        for (var i = 0; i < callSites; i++)
        {
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

namespace StringSyntaxAttributeAnalyzer.Benchmarks;

[MemoryDiagnoser]
public class AnalyzerBenchmarks
{
    [Params(10, 100, 500)]
    public int CallSites;

    CSharpCompilation compilation = null!;
    ImmutableArray<DiagnosticAnalyzer> analyzers;

    [GlobalSetup]
    public void Setup()
    {
        var source = SourceBuilder.Build(CallSites);

        var tree = CSharpSyntaxTree.ParseText(source);
        var references = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Where(_ => _.Length > 0)
            .Select(_ => (MetadataReference)MetadataReference.CreateFromFile(_))
            .ToArray();

        compilation = CSharpCompilation.Create(
            "Bench",
            [tree],
            references,
            new(OutputKind.DynamicallyLinkedLibrary));

        analyzers = [new MismatchAnalyzer()];
    }

    // Baseline: compiler work with no analyzer attached. Subtracting this from
    // Analyze shows the marginal cost the analyzer itself adds.
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
}

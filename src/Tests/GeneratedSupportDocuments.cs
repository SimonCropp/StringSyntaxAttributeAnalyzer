// The documents SyntaxConstantsGenerator adds to a consumer project.
//
// Codefix tests run against an AdhocWorkspace, which does not run generators from a
// ProjectReference, so the generator is executed here and its output added as plain
// documents. Running the real generator — rather than hand-copying a trimmed version
// of its output into each test file, which is what the per-file helpers used to do —
// means the `Syntax` constants, the attribute definitions and the global usings are
// exactly what a consumer gets. A fix that writes `Syntax.Json` is therefore checked
// against the real constant list, and the `EmitGlobalUsings=false` shape under test is
// the real one rather than "no support document at all".
static class GeneratedSupportDocuments
{
    static readonly Dictionary<(bool GlobalUsings, bool Shortcuts), ImmutableArray<(string Name, string Text)>> cache = [];

    public static ImmutableArray<(string Name, string Text)> For(bool globalUsings, bool shortcuts)
    {
        lock (cache)
        {
            if (cache.TryGetValue((globalUsings, shortcuts), out var cached))
            {
                return cached;
            }

            var generated = Run(globalUsings, shortcuts);
            cache[(globalUsings, shortcuts)] = generated;
            return generated;
        }
    }

    static ImmutableArray<(string Name, string Text)> Run(bool globalUsings, bool shortcuts)
    {
        var compilation = CSharpCompilation.Create(
            "GeneratorHost",
            [CSharpSyntaxTree.ParseText("class GeneratorHost;")],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));

        // Only "false" opts out of globals and only "true" opts in to shortcuts, so the
        // defaults are expressed by leaving the property unset rather than by its inverse.
        var options = new OptOutOptionsProvider(
            globalUsings ? null : "false",
            shortcuts ? "true" : null);

        var driver = CSharpGeneratorDriver.Create(
            generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: options);

        var result = driver.RunGenerators(compilation).GetRunResult();
        return [..result.GeneratedTrees.Select(_ => (Path.GetFileName(_.FilePath), _.ToString()))];
    }
}

[TestFixture]
public class SyntaxConstantsGeneratorTests
{
    [Test]
    public void EmitsSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");

        AreEqual(0, runResult.Diagnostics.Length);

        var typesTree = runResult.GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("Syntax.Types.g.cs"));
        var types = typesTree.ToString();
        IsTrue(types.Contains("static class Syntax"));
        IsTrue(types.Contains("public const string Json = SyntaxAttribute.Json;"));
        IsTrue(types.Contains("public const string Html = nameof(Html);"));

        var globalsTree = runResult.GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("Syntax.Globals.g.cs"));
        IsTrue(globalsTree.ToString().Contains("global using System.Diagnostics.CodeAnalysis;"));
    }

    [Test]
    public void ConsumerCode_NoLocalUsing_CompilesDueToGlobalUsing()
    {
        // No `using System.Diagnostics.CodeAnalysis;` anywhere in the user source —
        // the generator's `global using` should make StringSyntax available.
        var source =
            """
            public class Target
            {
                public static void Consume([StringSyntax("Regex")] string value) { }
            }
            """;

        var compilation = BuildCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var genDiagnostics);

        AreEqual(0, genDiagnostics.Length);

        var errors = updated.GetDiagnostics()
            .Where(_ => _.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AreEqual(
            0,
            errors.Length,
            string.Join("\n", errors.Select(_ => _.ToString())));
    }

    [Test]
    public void ConsumerCodeUsingSyntaxConstants_Compiles()
    {
        var source =
            """
            using System.Diagnostics.CodeAnalysis;
            using StringSyntaxAttributeAnalyzer;

            public class Target
            {
                public static void Consume([StringSyntax(Syntax.Regex)] string value) { }
            }

            public class Holder
            {
                [StringSyntax(Syntax.Regex)]
                public string Pattern { get; set; } = "";

                public void Use() => Target.Consume(Pattern);
            }
            """;

        var compilation = BuildCompilation(source);
        var driver = CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        driver.RunGeneratorsAndUpdateCompilation(compilation, out var updated, out var genDiagnostics);

        AreEqual(0, genDiagnostics.Length);

        var errors = updated.GetDiagnostics()
            .Where(_ => _.Severity == DiagnosticSeverity.Error)
            .ToArray();
        AreEqual(
            0,
            errors.Length,
            string.Join("\n", errors.Select(_ => _.ToString())));
    }

    [Test]
    public void EmitGlobalUsings_FalseFromMsBuild_SuppressesGlobalsFile()
    {
        var compilation = BuildCompilation("public class Dummy {}");
        var options = new OptOutOptionsProvider("false");

        var driver = CSharpGeneratorDriver.Create(
            generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: options);

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        AreEqual(0, runResult.Diagnostics.Length);
        IsTrue(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Types.g.cs")),
            "Types file should still be generated when globals are opted out");
        IsFalse(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs")),
            "Globals file should NOT be generated when StringSyntaxAnalyzer_EmitGlobalUsings=false");
    }

    [Test]
    public void EmitGlobalUsings_OtherValue_StillEmitsGlobals()
    {
        // Only "false" (case-insensitive) opts out; anything else leaves globals on.
        var compilation = BuildCompilation("public class Dummy {}");
        var options = new OptOutOptionsProvider("true");

        var driver = CSharpGeneratorDriver.Create(
            generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: options);

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        IsTrue(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs")));
    }

    sealed class OptOutOptionsProvider(string emitGlobalUsingsValue) : AnalyzerConfigOptionsProvider
    {
        public override AnalyzerConfigOptions GlobalOptions { get; } =
            new OptOutOptions(emitGlobalUsingsValue);

        public override AnalyzerConfigOptions GetOptions(SyntaxTree tree) => GlobalOptions;

        public override AnalyzerConfigOptions GetOptions(AdditionalText additionalText) => GlobalOptions;
    }

    sealed class OptOutOptions(string emitGlobalUsingsValue) : AnalyzerConfigOptions
    {
        public override bool TryGetValue(string key, [NotNullWhen(true)] out string? value)
        {
            if (key == "build_property.StringSyntaxAnalyzer_EmitGlobalUsings")
            {
                value = emitGlobalUsingsValue;
                return true;
            }

            value = null;
            return false;
        }
    }

    static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = BuildCompilation(source);

        var driver = CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    static CSharpCompilation BuildCompilation(string source)
    {
        var trusted = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));

        return CSharpCompilation.Create(
            "Tests",
            [CSharpSyntaxTree.ParseText(source)],
            trusted,
            new(OutputKind.DynamicallyLinkedLibrary));
    }
}

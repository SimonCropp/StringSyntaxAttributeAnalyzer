[TestFixture]
public class SyntaxConstantsGeneratorTests
{
    [Test]
    public void EmitsSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");

        AreEqual(0, runResult.Diagnostics.Length);

        var generated = runResult.GeneratedTrees
            .Single(tree => tree.FilePath.EndsWith("Syntax.g.cs"));
        var text = generated.ToString();

        IsTrue(text.Contains("static class Syntax"));
        IsTrue(text.Contains("public const string Json = SyntaxAttribute.Json;"));
        IsTrue(text.Contains("public const string Html = nameof(Html);"));
    }

    [Test]
    public void ConsumerCodeUsingSyntaxConstants_Compiles()
    {
        var source = """
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

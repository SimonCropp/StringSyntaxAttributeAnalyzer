public class SyntaxConstantsGeneratorTests
{
    [Test]
    public async Task EmitsSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");

        await Assert.That(runResult.Diagnostics.Length).IsEqualTo(0);

        var typesTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"));
        var types = typesTree.ToString();
        await Assert.That(types.Contains("static class Syntax")).IsTrue();
        await Assert.That(types.Contains("public const string Json = StringSyntaxAttribute.Json;")).IsTrue();
        await Assert.That(types.Contains("public const string Html = nameof(Html);")).IsTrue();

        var globalsTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs"));
        await Assert.That(globalsTree.ToString().Contains("global using System.Diagnostics.CodeAnalysis;")).IsTrue();
    }

    [Test]
    public async Task ConsumerCode_NoLocalUsing_CompilesDueToGlobalUsing()
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

        await Assert.That(genDiagnostics.Length).IsEqualTo(0);

        var errors = updated.GetDiagnostics()
            .Where(_ => _.Severity == DiagnosticSeverity.Error)
            .ToArray();
        await Assert.That(errors.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ConsumerCodeUsingSyntaxConstants_Compiles()
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

        await Assert.That(genDiagnostics.Length).IsEqualTo(0);

        var errors = updated.GetDiagnostics()
            .Where(_ => _.Severity == DiagnosticSeverity.Error)
            .ToArray();
        await Assert.That(errors.Length).IsEqualTo(0);
    }

    [Test]
    public async Task EmitGlobalUsings_FalseFromMsBuild_SuppressesGlobalsFile()
    {
        var compilation = BuildCompilation("public class Dummy {}");
        var options = new OptOutOptionsProvider("false");

        var driver = CSharpGeneratorDriver.Create(
            generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: options);

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        await Assert.That(runResult.Diagnostics.Length).IsEqualTo(0);
        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"))).IsTrue();
        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs"))).IsFalse();
    }

    [Test]
    public async Task EmitGlobalUsings_OtherValue_StillEmitsGlobals()
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

        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs"))).IsTrue();
    }
    // Guards against adding a new constant to the generator's Syntax class and
    // forgetting to mirror it in KnownSyntaxConstants — the codefix would silently
    // degrade to emitting a string literal instead of `Syntax.<name>`.
    [Test]
    public async Task KnownSyntaxConstants_MatchesGeneratedSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");
        var typesTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"));

        var syntaxClass = (await typesTree.GetRootAsync())
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(_ => _.Identifier.Text == "Syntax");

        var generatedNames = syntaxClass.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(_ => _.Declaration.Variables.Select(_ => _.Identifier.Text))
            .ToImmutableHashSet();

        var missing = generatedNames.Except(KnownSyntaxConstants.Names);
        await Assert.That(missing.IsEmpty).IsTrue();

        var stale = KnownSyntaxConstants.Names.Except(generatedNames);
        await Assert.That(stale.IsEmpty).IsTrue();
    }

    [Test]
    public async Task EmitShortcutAttributes_NotSet_NoShortcutsFile()
    {
        var runResult = RunGenerator("public class Dummy {}");
        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Shortcuts.g.cs"))).IsFalse();
    }

    [Test]
    public async Task SkipsTypesEmit_WhenAttributeVisibleFromReference()
    {
        // Simulate an upstream assembly that already exposes
        // StringSyntaxAttributeAnalyzer.UnionSyntaxAttribute (public here stands
        // in for InternalsVisibleTo).
        var upstreamSource =
            """
            namespace StringSyntaxAttributeAnalyzer;
            using System;
            public sealed class UnionSyntaxAttribute(params string[] options) : Attribute;
            public static class Syntax
            {
                public const string Html = nameof(Html);
            }
            """;
        var upstream = CSharpCompilation.Create(
            "Upstream",
            [CSharpSyntaxTree.ParseText(upstreamSource)],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));

        using var peStream = new MemoryStream();
        var emitResult = upstream.Emit(peStream);
        await Assert.That(emitResult.Success).IsTrue();
        peStream.Position = 0;
        var upstreamRef = MetadataReference.CreateFromStream(peStream);

        var compilation = CSharpCompilation.Create(
            "Downstream",
            [CSharpSyntaxTree.ParseText("public class Dummy {}")],
            [.. TrustedPlatformReferences.All, upstreamRef],
            new(OutputKind.DynamicallyLinkedLibrary));

        var driver = CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        var runResult = driver.RunGenerators(compilation).GetRunResult();

        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"))).IsFalse();
        await Assert.That(runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs"))).IsTrue();
    }

    [Test]
    public async Task EmitShortcutAttributes_True_EmitsShortcutsForKnownConstants()
    {
        var compilation = BuildCompilation("public class Dummy {}");
        var options = new OptOutOptionsProvider(emitShortcutAttributesValue: "true");

        var driver = CSharpGeneratorDriver.Create(
            generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
            additionalTexts: [],
            parseOptions: null,
            optionsProvider: options);

        var runResult = driver.RunGenerators(compilation).GetRunResult();

        var shortcutsTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Shortcuts.g.cs"));
        var text = shortcutsTree.ToString();
        await Assert.That(text.Contains("namespace StringSyntaxAttributeAnalyzer")).IsTrue();
        await Assert.That(text.Contains("sealed class HtmlAttribute")).IsTrue();
        await Assert.That(text.Contains("sealed class JsonAttribute")).IsTrue();
        await Assert.That(text.Contains("sealed class RegexAttribute")).IsTrue();
    }

    static GeneratorDriverRunResult RunGenerator(string source)
    {
        var compilation = BuildCompilation(source);

        var driver = CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        return driver.RunGenerators(compilation).GetRunResult();
    }

    static CSharpCompilation BuildCompilation(string source) =>
        CSharpCompilation.Create(
            "Tests",
            [CSharpSyntaxTree.ParseText(source)],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));
}

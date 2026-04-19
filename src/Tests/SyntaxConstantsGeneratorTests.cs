[TestFixture]
public class SyntaxConstantsGeneratorTests
{
    [Test]
    public void EmitsSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");

        AreEqual(0, runResult.Diagnostics.Length);

        var typesTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"));
        var types = typesTree.ToString();
        IsTrue(types.Contains("static class Syntax"));
        IsTrue(types.Contains("public const string Json = StringSyntaxAttribute.Json;"));
        IsTrue(types.Contains("public const string Html = nameof(Html);"));

        var globalsTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Globals.g.cs"));
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
            string.Join('\n', errors.Select(_ => _.ToString())));
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
            string.Join('\n', errors.Select(_ => _.ToString())));
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
    // Guards against adding a new constant to the generator's Syntax class and
    // forgetting to mirror it in KnownSyntaxConstants — the codefix would silently
    // degrade to emitting a string literal instead of `Syntax.<name>`.
    [Test]
    public void KnownSyntaxConstants_MatchesGeneratedSyntaxClass()
    {
        var runResult = RunGenerator("public class Dummy {}");
        var typesTree = runResult.GeneratedTrees
            .Single(_ => _.FilePath.EndsWith("Syntax.Types.g.cs"));

        var syntaxClass = typesTree.GetRoot()
            .DescendantNodes()
            .OfType<ClassDeclarationSyntax>()
            .Single(_ => _.Identifier.Text == "Syntax");

        var generatedNames = syntaxClass.Members
            .OfType<FieldDeclarationSyntax>()
            .SelectMany(_ => _.Declaration.Variables.Select(v => v.Identifier.Text))
            .ToImmutableHashSet();

        var missing = generatedNames.Except(KnownSyntaxConstants.Names);
        IsTrue(
            missing.IsEmpty,
            $"KnownSyntaxConstants is missing: {string.Join(", ", missing)}");

        var stale = KnownSyntaxConstants.Names.Except(generatedNames);
        IsTrue(
            stale.IsEmpty,
            $"KnownSyntaxConstants has entries not in the generator: {string.Join(", ", stale)}");
    }

    [Test]
    public void EmitShortcutAttributes_NotSet_NoShortcutsFile()
    {
        var runResult = RunGenerator("public class Dummy {}");
        IsFalse(
            runResult.GeneratedTrees.Any(_ => _.FilePath.EndsWith("Syntax.Shortcuts.g.cs")),
            "Shortcuts file should be opt-in; default compilation must not emit it");
    }

    [Test]
    public void EmitShortcutAttributes_True_EmitsShortcutsForKnownConstants()
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
        IsTrue(text.Contains("namespace StringSyntaxAttributeAnalyzer"));
        IsTrue(text.Contains("sealed class HtmlAttribute"));
        IsTrue(text.Contains("sealed class JsonAttribute"));
        IsTrue(text.Contains("sealed class RegexAttribute"));
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

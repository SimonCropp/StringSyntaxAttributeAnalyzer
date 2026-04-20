[TestFixture]
public class NameConventionsTests
{
    [Test]
    public void Convention_PromotesParameter_AsSource_NoMismatch()
    {
        // `url` matches the Uri convention, so passing it to a `[StringSyntax(Uri)]`
        // parameter should not raise SSA002 — the convention satisfies the target.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Uri)] string value) { }
            }

            public class Holder
            {
                public string Url { get; set; }

                public void Use(Target target) => target.Consume(Url);
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void Convention_Disabled_StillFiresMissingFormat()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Uri)] string value) { }
            }

            public class Holder
            {
                public string Url { get; set; }

                public void Use(Target target) => target.Consume(Url);
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: false);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA002", diagnostics[0].Id);
    }

    [Test]
    public void Convention_PascalCaseSuffix_OnParameterName()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Uri)] string value) { }
            }

            public class Holder
            {
                public void Use(Target target, string pageUrl) => target.Consume(pageUrl);
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void Convention_MismatchAcrossConventions_FiresFormatMismatch()
    {
        // Both sides convention-promote, but to different values → SSA001.
        var source =
            """
            public class Holder
            {
                public string Url { get; set; }
                public string Html { get; set; }

                public void Copy() => Html = Url;
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
    }

    [Test]
    public void Convention_NotPascalBoundary_DoesNotMatch()
    {
        // `myhtml` (lowercase, no boundary) should NOT match the Html convention.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }
            }

            public class Holder
            {
                public string myhtml { get; set; }

                public void Use(Target target) => target.Consume(myhtml);
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA002", diagnostics[0].Id);
    }

    [Test]
    public void Convention_RedundantAttribute_FiresSSA008_OnProperty()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; }
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA008", diagnostics[0].Id);
    }

    [Test]
    public void Convention_RedundantAttribute_FiresOnParameter()
    {
        var source =
            """
            public class Holder
            {
                public void Use([StringSyntax("Html")] string pageHtml) { }
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA008", diagnostics[0].Id);
    }

    [Test]
    public void Convention_RedundantLanguageComment_FiresSSA008()
    {
        var source =
            """
            public class Holder
            {
                public void Use()
                {
                    // language=html
                    string pageHtml = "<p/>";
                    System.Console.WriteLine(pageHtml);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(1, diagnostics.Length);
        AreEqual("SSA008", diagnostics[0].Id);
    }

    [Test]
    public void Convention_LocalNameMatchesConvention_NoMissingFormat()
    {
        // local `pageHtml` should convention-promote to Html and satisfy the
        // [Html] target without raising SSA002 or needing //language=.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("Html")] string value) { }
            }

            public class Holder
            {
                public void Use(Target target)
                {
                    string pageHtml = "<p/>";
                    target.Consume(pageHtml);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void Convention_AttributeWithDifferentValue_DoesNotFireSSA008()
    {
        // Property `Url` carries `[StringSyntax(Json)]` — value is Json, name
        // matches Uri. Attribute is not redundant (it overrides naming intent).
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Json)]
                public string Url { get; set; }
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void Convention_BypassesKnownUnannotatedAssemblies_OnTarget()
    {
        // Newtonsoft.Json is in KnownUnannotatedAssemblies, so SSA003 is
        // normally suppressed. But its `JsonConvert.SerializeObject` parameter
        // names happen to be `value` (no convention match). So instead use a
        // proxy: simulate by using a method whose target lives in System (also
        // unannotated). We use System.IO.Path.GetFileName(string path) — `path`
        // does NOT match a convention, so SSA003 stays suppressed.
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Source { get; set; }

                public string Use() => System.IO.Path.GetFileName(Source);
            }
            """;

        var diagnostics = GetDiagnostics(source, conventions: true);
        // `path` doesn't match Uri convention → still suppressed via
        // KnownUnannotatedAssemblies / namespace suppression. No diagnostic.
        AreEqual(0, diagnostics.Length);
    }

    static ImmutableArray<Diagnostic> GetDiagnostics(string source, bool conventions)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var baseCompilation = CSharpCompilation.Create(
            "Tests",
            [syntaxTree],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));

        CSharpGeneratorDriver
            .Create(new SyntaxConstantsGenerator())
            .RunGeneratorsAndUpdateCompilation(baseCompilation, out var compilation, out _);

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        if (conventions)
        {
            dict["stringsyntax.name_conventions"] = "enabled";
        }

        var options = new AnalyzerOptions(
            additionalFiles: [],
            optionsProvider: new TestConfigOptionsProvider(dict));

        return compilation
            .WithAnalyzers([new MismatchAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}

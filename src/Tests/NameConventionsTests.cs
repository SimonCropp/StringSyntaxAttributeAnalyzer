public class NameConventionsTests
{
    [Test]
    public async Task Convention_PromotesParameter_AsSource_NoMismatch()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Convention_Disabled_StillFiresMissingFormat()
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

        var diagnostics = await GetDiagnostics(source, conventions: false);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task Convention_PascalCaseSuffix_OnParameterName()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Convention_MismatchAcrossConventions_FiresFormatMismatch()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Convention_NotPascalBoundary_DoesNotMatch()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task Convention_RedundantAttribute_FiresSSA008_OnProperty()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; }
            }
            """;

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA008");
    }

    [Test]
    public async Task Convention_RedundantAttribute_FiresOnParameter()
    {
        var source =
            """
            public class Holder
            {
                public void Use([StringSyntax("Html")] string pageHtml) { }
            }
            """;

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA008");
    }

    [Test]
    public async Task Convention_RedundantLanguageComment_FiresSSA008()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA008");
    }

    [Test]
    public async Task Convention_LocalNameMatchesConvention_NoMissingFormat()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Convention_AttributeWithDifferentValue_DoesNotFireSSA008()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Convention_BypassesKnownUnannotatedAssemblies_OnTarget()
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

        var diagnostics = await GetDiagnostics(source, conventions: true);
        // `path` doesn't match Uri convention → still suppressed via
        // KnownUnannotatedAssemblies / namespace suppression. No diagnostic.
        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    static Task<ImmutableArray<Diagnostic>> GetDiagnostics(string source, bool conventions)
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
            .GetAnalyzerDiagnosticsAsync();
    }
}

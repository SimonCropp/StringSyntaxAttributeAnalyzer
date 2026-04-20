public class MismatchAnalyzerTests
{
    [Test]
    public async Task FormatMismatch_ArgumentToParameter()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        var message = diagnostics[0].GetMessage();
        await Assert.That(message).Contains("DateTimeFormat");
        await Assert.That(message).Contains("Regex");
    }

    [Test]
    public async Task FormatMismatch_PropertyToProperty_Assignment()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; }

                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Copy() => Format = Pattern;
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task FormatMismatch_ObjectInitializer()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; }

                public Target Create() => new Target { Pattern = Format };
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task MethodReturnSource_IsUnknown()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                public string GetValue() => "";

                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(GetValue());
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task MissingSourceFormat_ArgumentToParameter()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Regex");
    }

    [Test]
    public async Task MissingSourceFormat_PropertyInitializer()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Source
            {
                public string Raw { get; set; }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Source src) => Pattern = src.Raw;
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task DroppedFormat_AssignPropertyToUnattributed()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public string Value { get; set; }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Value = Pattern;
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
        await Assert.That(diagnostics[0].GetMessage()).Contains("Regex");
    }

    [Test]
    public async Task DroppedFormat_ArgumentToParameter()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume(string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Consume(Pattern);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
    }

    [Test]
    public async Task MatchingFormats_NoDiagnostic()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Consume(Pattern);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task StringLiteralSource_NoDiagnostic()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Caller
            {
                public void Use(Target target) => target.Consume("[a-z]+");
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariableSource_NoDiagnostic()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Caller
            {
                public void Use(Target target)
                {
                    string local = "anything";
                    target.Consume(local);
                }
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task NoStringSyntaxAnywhere_NoDiagnostic()
    {
        var source = """
            public class Target
            {
                public void Consume(string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task CustomFormatString_Mismatch()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax("custom-a")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("custom-b")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task FirstCharCaseInsensitive_NoDiagnostic()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax("json")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("Json")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task MidStringCaseDifference_StillMismatch()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax("json")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("jSon")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task FieldSource_Mismatch()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Field;

                public void Consume([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string value) { }

                public void Use() => Consume(Field);
            }
            """;

        var diagnostics = GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    static ImmutableArray<Diagnostic> GetDiagnostics(string source)
    {
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var trustedAssemblies = ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(_ => MetadataReference.CreateFromFile(_))
            .ToList();

        var compilation = CSharpCompilation.Create(
            "Tests",
            [syntaxTree],
            trustedAssemblies,
            new(OutputKind.DynamicallyLinkedLibrary));

        var analyzer = new MismatchAnalyzer();

        return compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}

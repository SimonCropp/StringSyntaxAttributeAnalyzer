using System.Collections.Immutable;
using Microsoft.CodeAnalysis.Diagnostics;

[TestFixture]
public class MismatchAnalyzerTests
{
    [Test]
    public void FormatMismatch_ArgumentToParameter()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
        var message = diagnostics[0].GetMessage();
        IsTrue(message.Contains("DateTimeFormat"));
        IsTrue(message.Contains("Regex"));
    }

    [Test]
    public void FormatMismatch_PropertyToProperty_Assignment()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
    }

    [Test]
    public void FormatMismatch_ObjectInitializer()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
    }

    [Test]
    public void MethodReturnSource_IsUnknown()
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

        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void MissingSourceFormat_ArgumentToParameter()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA002", diagnostics[0].Id);
        IsTrue(diagnostics[0].GetMessage().Contains("Regex"));
    }

    [Test]
    public void MissingSourceFormat_PropertyInitializer()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA002", diagnostics[0].Id);
    }

    [Test]
    public void DroppedFormat_AssignPropertyToUnattributed()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA003", diagnostics[0].Id);
        IsTrue(diagnostics[0].GetMessage().Contains("Regex"));
    }

    [Test]
    public void DroppedFormat_ArgumentToParameter()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA003", diagnostics[0].Id);
    }

    [Test]
    public void MatchingFormats_NoDiagnostic()
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

        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void StringLiteralSource_NoDiagnostic()
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

        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void LocalVariableSource_NoDiagnostic()
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

        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void NoStringSyntaxAnywhere_NoDiagnostic()
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

        AreEqual(0, diagnostics.Length);
    }

    [Test]
    public void CustomFormatString_Mismatch()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
    }

    [Test]
    public void FieldSource_Mismatch()
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

        AreEqual(1, diagnostics.Length);
        AreEqual("SSA001", diagnostics[0].Id);
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

        var analyzer = new StringSyntaxAttributeAnalyzer.MismatchAnalyzer();

        return compilation
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync()
            .GetAwaiter()
            .GetResult();
    }
}

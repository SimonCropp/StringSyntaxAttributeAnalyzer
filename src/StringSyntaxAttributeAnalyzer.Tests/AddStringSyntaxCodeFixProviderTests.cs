[TestFixture]
public class AddStringSyntaxCodeFixProviderTests
{
    [Test]
    public async Task SSA002_AddsAttributeToSourceProperty()
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

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceField()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                public string Field;

                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(Field);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        Contains(fixedSource, "public string Field;");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceParameter()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use(string input) => Consume(input);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")] string input");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetParameter()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public static void Consume(string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => Target.Consume(Pattern);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")] string value");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetProperty()
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

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA002_CustomFormatValue()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                public string Value { get; set; }

                public static void Consume([StringSyntax("custom-format")] string value) { }

                public void Use() => Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[StringSyntax(\"custom-format\")]");
    }

    [Test]
    public async Task MultiDeclaratorField_NoFixRegistered()
    {
        var source = """
            using System.Diagnostics.CodeAnalysis;

            public class Holder
            {
                public string a, b;

                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(a);
            }
            """;

        var actions = await GetCodeActions(source);

        AreEqual(0, actions.Length);
    }

    static void Contains(string actual, string expected) =>
        IsTrue(
            actual.Contains(expected),
            $"Expected fixed source to contain:\n{expected}\n\nActual:\n{actual}");

    static async Task<string> ApplyFix(string source)
    {
        var (document, diagnostic) = await PrepareFixAsync(source);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().Single();

        var newDocument = applyOperation.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDocument.GetTextAsync();
        return text.ToString();
    }

    static async Task<ImmutableArray<CodeAction>> GetCodeActions(string source)
    {
        var (document, diagnostic) = await PrepareFixAsync(source);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions.ToImmutable();
    }

    static async Task<(Document Document, Diagnostic Diagnostic)> PrepareFixAsync(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TrustedReferences());

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var documentId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution.AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        return (document, diagnostics.Single());
    }

    static IEnumerable<MetadataReference> TrustedReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
}

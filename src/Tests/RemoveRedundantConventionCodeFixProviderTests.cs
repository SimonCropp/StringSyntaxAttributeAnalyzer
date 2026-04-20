public class RemoveRedundantConventionCodeFixProviderTests
{
    [Test]
    public async Task SSA008_RemovesAttribute_WhenSoleAttributeOnProperty()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "StringSyntax");
        await Contains(fixedSource, "public string Url { get; set; }");
    }

    [Test]
    public async Task SSA008_RemovesAttribute_OnParameter()
    {
        var source =
            """
            public class Holder
            {
                public void Use([StringSyntax("Html")] string pageHtml) { }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "StringSyntax");
        await Contains(fixedSource, "string pageHtml");
    }

    [Test]
    public async Task SSA008_RemovesLanguageComment_PrecedingLine()
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

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "language=");
        await Contains(fixedSource, "string pageHtml");
    }

    static async Task Contains(string actual, string expected) =>
        await Assert.That(actual).Contains(expected);

    static async Task DoesNotContain(string actual, string unexpected) =>
        await Assert.That(actual).DoesNotContain(unexpected);

    static async Task<string> ApplyFix(string source)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TrustedPlatformReferences.All);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var documentId = DocumentId.CreateNewId(projectInfo.Id);
        var generatedId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution
            .AddDocument(generatedId, "Generated.cs", """
                global using System.Diagnostics.CodeAnalysis;
                global using StringSyntaxAttributeAnalyzer;
                """)
            .AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;

        var dict = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase)
        {
            ["stringsyntax.name_conventions"] = "enabled",
        };
        var options = new AnalyzerOptions(
            additionalFiles: [],
            optionsProvider: new TestConfigOptionsProvider(dict));

        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()], options)
            .GetAnalyzerDiagnosticsAsync();

        var diagnostic = diagnostics.Single(_ => _.Id == "SSA008");

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new RemoveRedundantConventionCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().Single();

        var newDocument = applyOperation.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDocument.GetTextAsync();
        return text.ToString();
    }
}

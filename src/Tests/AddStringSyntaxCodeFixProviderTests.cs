[TestFixture]
public class AddStringSyntaxCodeFixProviderTests
{
    [Test]
    public async Task SSA002_AddsAttributeToSourceProperty()
    {
        var source =
            """
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

        Contains(fixedSource, "[Syntax(\"Regex\")]");
        Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceField()
    {
        var source =
            """
            public class Holder
            {
                public string Field;

                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(Field);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[Syntax(\"Regex\")]");
        Contains(fixedSource, "public string Field;");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceParameter()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use(string input) => Consume(input);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[Syntax(\"Regex\")] string input");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetParameter()
    {
        var source =
            """
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

        Contains(fixedSource, "[Syntax(\"Regex\")] string value");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetProperty()
    {
        var source =
            """
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

        Contains(fixedSource, "[Syntax(\"Regex\")]");
        Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA005_AddsAttributeToUnattributedEqualitySide()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public string Raw { get; set; }

                public bool Check() => Pattern == Raw;
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[Syntax(\"Regex\")]");
        Contains(fixedSource, "public string Raw { get; set; }");
    }

    [Test]
    public async Task SSA006_ReplacesSingletonUnionWithStringSyntax()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html")]
                public string Markup { get; set; }
            }
            """;

        var fixedSource = await ApplyFix<ReplaceUnionWithStringSyntaxCodeFixProvider>(source);

        Contains(fixedSource, "[Syntax(\"html\")]");
        IsFalse(fixedSource.Contains("[UnionSyntax"), $"Expected UnionSyntax removed:\n{fixedSource}");
    }

    [Test]
    public async Task Fix_WithoutSyntaxAlias_FallsBackToStringSyntax()
    {
        // Consumer opted out of the generator's global usings — no `SyntaxAttribute` alias
        // in scope. The fixer should emit the long form `[StringSyntax(...)]` so the
        // result compiles.
        var source =
            """
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

        var fixedSource = await ApplyFixWithoutAlias(source);

        Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        IsFalse(fixedSource.Contains("[Syntax("),
            $"Expected no [Syntax(...)] attribute when alias isn't in scope:\n{fixedSource}");
    }

    static async Task<string> ApplyFixWithoutAlias(string source)
    {
        // Same shape as PrepareFixAsync but deliberately omits the
        // `global using SyntaxAttribute = ...` alias — simulates the opt-out case.
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
        var diagnostic = diagnostics.Single();

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);
        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var newDoc = apply.ChangedSolution.GetDocument(document.Id)!;
        return (await newDoc.GetTextAsync()).ToString();
    }

    [Test]
    public async Task SSA002_CustomFormatValue()
    {
        var source =
            """
            public class Holder
            {
                public string Value { get; set; }

                public static void Consume([StringSyntax("custom-format")] string value) { }

                public void Use() => Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Contains(fixedSource, "[Syntax(\"custom-format\")]");
    }

    [Test]
    public async Task MultiDeclaratorField_NoFixRegistered()
    {
        var source =
            """
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

    [Test]
    public async Task SSA002_ExactOutputShape_WithExistingUsings()
    {
        var source =
            """
            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed source should not start with a newline");
        IsFalse(fixedSource.StartsWith('\r'), "Fixed source should not start with CR");
    }

    [Test]
    public async Task SSA002_WithGlobalUsing_DoesNotAddLocalUsing()
    {
        // Consumer has `global using System.Diagnostics.CodeAnalysis;` in another file, so
        // the fix-target file never had its own local using. A naive codefix would add a
        // redundant local using which later tooling strips, leaving blank trivia.
        var targetSource =
            """
            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;
        var globalUsings = "global using System.Diagnostics.CodeAnalysis;\n";

        var fixedSource = await ApplyFixWithGlobalUsings(targetSource, globalUsings);

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed file should not start with a newline");
        IsFalse(
            fixedSource.Contains("using System.Diagnostics.CodeAnalysis;"),
            "Should NOT have added a local using when the namespace is already in a global using");
    }

    static async Task<string> ApplyFixWithGlobalUsings(string source, string globalUsings)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
                .Split(Path.PathSeparator)
                .Select(path => MetadataReference.CreateFromFile(path)));

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var globalsId = DocumentId.CreateNewId(projectInfo.Id);
        var targetId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution
            .AddDocument(globalsId, "GlobalUsings.cs", globalUsings)
            .AddDocument(targetId, "Target.cs", source);

        var targetDoc = solution.GetDocument(targetId)!;
        var compilation = (await targetDoc.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.Single();

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            targetDoc,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);
        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var newDoc = apply.ChangedSolution.GetDocument(targetId)!;
        return (await newDoc.GetTextAsync()).ToString();
    }

    [Test]
    public async Task SSA002_ExactOutputShape_NoExistingUsings()
    {
        var source =
            """
            public class Target
            {
                public static void Consume([System.Diagnostics.CodeAnalysis.StringSyntax("Regex")] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed source should not start with a newline");
        IsFalse(fixedSource.StartsWith('\r'), "Fixed source should not start with CR");
    }

    static void Contains(string actual, string expected) =>
        IsTrue(
            actual.Contains(expected),
            $"Expected fixed source to contain:\n{expected}\n\nActual:\n{actual}");

    static Task<string> ApplyFix(string source) =>
        ApplyFix<AddStringSyntaxCodeFixProvider>(source);

    static async Task<string> ApplyFix<TProvider>(string source)
        where TProvider : CodeFixProvider, new()
    {
        var (document, diagnostic) = await PrepareFixAsync(source);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new TProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
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
            Cancel.None);

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
        var generatedId = DocumentId.CreateNewId(projectInfo.Id);
        // Mirror what SyntaxConstantsGenerator emits: the global using for
        // StringSyntaxAttribute, and the UnionSyntaxAttribute definition. Test sources
        // don't have to declare either. Duplicated here (rather than actually running
        // the generator) because AdhocWorkspace fix pipelines don't pick up generators
        // automatically from a project reference.
        solution = solution
            .AddDocument(generatedId, "Generated.cs", """
                global using System.Diagnostics.CodeAnalysis;
                global using StringSyntaxAttributeAnalyzer;
                global using SyntaxAttribute = System.Diagnostics.CodeAnalysis.StringSyntaxAttribute;

                namespace StringSyntaxAttributeAnalyzer
                {
                    [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                    internal sealed class UnionSyntaxAttribute(params string[] options) : System.Attribute;
                }
                """)
            .AddDocument(documentId, "Test.cs", source);

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

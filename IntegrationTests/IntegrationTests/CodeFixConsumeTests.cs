using System.Collections.Immutable;
using System.Reflection;
using Microsoft.CodeAnalysis;
using Microsoft.CodeAnalysis.CodeActions;
using Microsoft.CodeAnalysis.CodeFixes;
using Microsoft.CodeAnalysis.CSharp;
using Microsoft.CodeAnalysis.Diagnostics;

public class CodeFixConsumeTests
{
    // Verifies that the Release-built nuget (from ../nugets) actually ships a loadable
    // CodeFixes.dll under analyzers/dotnet/cs/, and that the provider applies correctly
    // when driven from that DLL — not just from the codefix project reference used by
    // the src/ unit tests.
    [Test]
    public async Task PackagedCodeFixProvider_AddsStringSyntaxAttribute_ForSSA002()
    {
        var (analyzer, codeFix) = await LoadAnalyzerAndCodeFixFromPackage();

        var source = """
            using System.Diagnostics.CodeAnalysis;

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

        var (document, diagnostic) = await CompileAndAnalyze(source, analyzer);

        await Assert.That(diagnostic.Id).IsEqualTo("SSA002");

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            CancellationToken.None);

        await codeFix.RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(CancellationToken.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var newDoc = apply.ChangedSolution.GetDocument(document.Id)!;
        var fixedText = (await newDoc.GetTextAsync()).ToString();

        await Assert.That(fixedText).Contains("[StringSyntax(\"Regex\")]");
    }

    static async Task<(DiagnosticAnalyzer Analyzer, CodeFixProvider CodeFix)> LoadAnalyzerAndCodeFixFromPackage()
    {
        var analyzersDir = FindAnalyzersDirectory();

        var analyzerPath = Path.Combine(analyzersDir, "StringSyntaxAttributeAnalyzer.dll");
        var codeFixPath = Path.Combine(analyzersDir, "StringSyntaxAttributeAnalyzer.CodeFixes.dll");

        await Assert.That(File.Exists(analyzerPath)).IsTrue();
        await Assert.That(File.Exists(codeFixPath)).IsTrue();

        var analyzerAsm = System.Reflection.Assembly.LoadFrom(analyzerPath);
        var codeFixAsm = System.Reflection.Assembly.LoadFrom(codeFixPath);

        var analyzerType = analyzerAsm.GetType("StringSyntaxAttributeAnalyzer.MismatchAnalyzer");
        var codeFixType = codeFixAsm.GetType("StringSyntaxAttributeAnalyzer.AddStringSyntaxCodeFixProvider");

        await Assert.That(analyzerType).IsNotNull();
        await Assert.That(codeFixType).IsNotNull();

        var analyzer = (DiagnosticAnalyzer)Activator.CreateInstance(analyzerType!)!;
        var codeFix = (CodeFixProvider)Activator.CreateInstance(codeFixType!)!;

        return (analyzer, codeFix);
    }

    static string FindAnalyzersDirectory()
    {
        var root = Environment.GetEnvironmentVariable("NUGET_PACKAGES")
            ?? Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".nuget",
                "packages");

        // AnalyzerPackageVersion is injected by the csproj via AssemblyMetadata, pinned to
        // $(Version). Scanning the package folder by name isn't enough — stale versions from
        // earlier local builds (e.g. 1.0.0) sort above the current one (0.1.1) lexically.
        var version = System.Reflection.Assembly.GetExecutingAssembly()
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .Single(_ => _.Key == "AnalyzerPackageVersion")
            .Value!;

        var versionDir = Path.Combine(root, "stringsyntaxattributeanalyzer", version);
        if (!Directory.Exists(versionDir))
        {
            throw new DirectoryNotFoundException($"Expected package directory missing: {versionDir}");
        }

        return Path.Combine(versionDir, "analyzers", "dotnet", "cs");
    }

    static async Task<(Document Document, Diagnostic Diagnostic)> CompileAndAnalyze(
        string source,
        DiagnosticAnalyzer analyzer)
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
            .WithAnalyzers([analyzer])
            .GetAnalyzerDiagnosticsAsync();

        return (document, diagnostics.Single());
    }

    static IEnumerable<MetadataReference> TrustedReferences() =>
        ((string)AppContext.GetData("TRUSTED_PLATFORM_ASSEMBLIES")!)
            .Split(Path.PathSeparator)
            .Select(path => MetadataReference.CreateFromFile(path));
}

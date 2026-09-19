// The single pipeline every codefix test goes through.
//
// Applying a fix and asserting `Contains(fixedSource, "[Syntax(Syntax.Regex)]")` only
// proves the fix wrote something somewhere. It cannot see a fix that also detached the
// member's doc comment, deleted the `#region` above it, wrote an attribute name that
// does not resolve, or left the warning it was registered against still standing — all
// of which produce output that still "contains" the expected string. So every apply
// runs two checks the per-file helpers used to leave to the individual test:
//
//  1. VerifyNoNewCompilerDiagnostics — the fixed document produces no compiler
//     diagnostic the original did not. Parse options use DocumentationMode.Diagnose so
//     a misplaced `///` surfaces as CS1587 rather than being silently dropped.
//     Compared as a before/after multiset rather than "no errors", because a test
//     source is allowed to carry warnings the fix is not responsible for.
//
//  2. VerifyDiagnosticCleared — re-running the analyzer over the fixed document no
//     longer reports the rule that was just fixed. A codefix that does not clear its
//     own diagnostic is broken by definition; other rules firing afterwards are not
//     this fix's problem and are ignored.
//
// Apply returns the whole document text, so callers assert on trivia and layout
// instead of on a fragment.
static class CodeFixVerify
{
    // Doc comments are only diagnosed when the parser is asked to. Without this a fix
    // that strands a `///` between the attribute list and the modifiers looks clean.
    static readonly CSharpParseOptions parseOptions =
        new(documentationMode: DocumentationMode.Diagnose);

    public static Task<string> Apply(string source, FixOptions? options = null) =>
        Apply<AddStringSyntaxCodeFixProvider>(source, options);

    public static async Task<string> Apply<TProvider>(string source, FixOptions? options = null)
        where TProvider : CodeFixProvider, new()
    {
        options ??= new();
        var prepared = await Prepare(source, options);
        var actions = await RegisterActions<TProvider>(prepared);

        await Assert.That(actions.Length).IsGreaterThan(options.ActionIndex);
        var action = actions[options.ActionIndex];

        var operations = await action.GetOperationsAsync(Cancel.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var fixedDocument = apply.ChangedSolution.GetDocument(prepared.Document.Id)!;
        var fixedText = (await fixedDocument.GetTextAsync()).ToString();

        // Verify against a document re-parsed from the fixed text, not against the tree
        // the fix handed back. A fix that deletes a `#region` leaves the in-memory tree's
        // directive structure intact, so the resulting CS1028 only shows up when the file
        // is read again — which is what the consumer actually hits.
        var reparsed = fixedDocument.WithText(SourceText.From(fixedText));

        await VerifyNoNewCompilerDiagnostics(prepared, reparsed);
        await VerifyDiagnosticCleared(prepared, reparsed);

        // The formatter breaks lines with Environment.NewLine, so on Windows a fix
        // splices CRLF into a document the repo stores as LF (.gitattributes sets
        // eol=lf). That is the workspace's newline option, not something the fix
        // chooses, so normalize rather than asserting around it — otherwise every
        // multi-line expectation only holds on one platform.
        return fixedText.Replace("\r\n", "\n");
    }

    // Registers the fixes without applying one — for tests that pin action titles or
    // assert that no fix is offered at all.
    public static async Task<ImmutableArray<CodeAction>> Actions<TProvider>(
        string source,
        FixOptions? options = null)
        where TProvider : CodeFixProvider, new()
    {
        var prepared = await Prepare(source, options ?? new());
        return await RegisterActions<TProvider>(prepared);
    }

    static async Task<ImmutableArray<CodeAction>> RegisterActions<TProvider>(PreparedFix prepared)
        where TProvider : CodeFixProvider, new()
    {
        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            prepared.Document,
            prepared.Diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new TProvider().RegisterCodeFixesAsync(context);
        return actions.ToImmutable();
    }

    static async Task VerifyNoNewCompilerDiagnostics(PreparedFix prepared, Document fixedDocument)
    {
        var compilation = (await fixedDocument.Project.GetCompilationAsync())!;
        var introduced = Introduced(prepared.CompilerDiagnostics, CompilerDiagnostics(compilation));

        // Asserting on the rendered text rather than on a count so the failure message
        // names the diagnostic instead of reading "expected 0 but found 1".
        await Assert.That(Describe(introduced)).IsEqualTo("");
    }

    static async Task VerifyDiagnosticCleared(PreparedFix prepared, Document fixedDocument)
    {
        var compilation = (await fixedDocument.Project.GetCompilationAsync())!;
        var remaining = (await compilation
                .WithAnalyzers([new MismatchAnalyzer()], prepared.AnalyzerOptions)
                .GetAnalyzerDiagnosticsAsync())
            .Where(_ => _.Id == prepared.Diagnostic.Id);

        await Assert.That(Describe(remaining)).IsEqualTo("");
    }

    static async Task<PreparedFix> Prepare(string source, FixOptions options)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            parseOptions: parseOptions,
            metadataReferences: TrustedPlatformReferences.All);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);

        if (options.SupportSource is { } supportSource)
        {
            solution = solution.AddDocument(
                DocumentId.CreateNewId(projectInfo.Id),
                "Support.cs",
                supportSource);
        }
        else
        {
            var support = GeneratedSupportDocuments.For(
                options.EmitGlobalUsings,
                options.EmitShortcutAttributes);
            foreach (var (name, text) in support)
            {
                solution = solution.AddDocument(
                    DocumentId.CreateNewId(projectInfo.Id),
                    name,
                    text);
            }
        }

        var documentId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution.AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;

        var analyzerOptions = new AnalyzerOptions(
            additionalFiles: [],
            optionsProvider: new TestConfigOptionsProvider(
                options.ConfigOptions ?? new(StringComparer.OrdinalIgnoreCase)));

        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()], analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();

        var filtered = options.DiagnosticId is null
            ? diagnostics
            : diagnostics.Where(_ => _.Id == options.DiagnosticId).ToImmutableArray();

        return new(
            document,
            filtered.Single(),
            CompilerDiagnostics(compilation),
            analyzerOptions);
    }

    static ImmutableArray<Diagnostic> CompilerDiagnostics(Compilation compilation) =>
        [..compilation
            .GetDiagnostics()
            .Where(_ => _.Severity >= DiagnosticSeverity.Warning)];

    // Multiset difference keyed on id + message. Location is deliberately excluded:
    // adding an attribute shifts every line below it, so comparing locations would
    // report the whole file as new.
    static ImmutableArray<Diagnostic> Introduced(
        ImmutableArray<Diagnostic> before,
        ImmutableArray<Diagnostic> after)
    {
        var counts = new Dictionary<string, int>();
        foreach (var diagnostic in before)
        {
            var key = Key(diagnostic);
            counts[key] = counts.TryGetValue(key, out var count) ? count + 1 : 1;
        }

        var builder = ImmutableArray.CreateBuilder<Diagnostic>();
        foreach (var diagnostic in after)
        {
            var key = Key(diagnostic);
            if (counts.TryGetValue(key, out var count) &&
                count > 0)
            {
                counts[key] = count - 1;
                continue;
            }

            builder.Add(diagnostic);
        }

        return builder.ToImmutable();
    }

    static string Key(Diagnostic diagnostic) =>
        $"{diagnostic.Id}: {diagnostic.GetMessage()}";

    static string Describe(IEnumerable<Diagnostic> diagnostics) =>
        string.Join(Environment.NewLine, diagnostics.Select(Key));

    sealed record PreparedFix(
        Document Document,
        Diagnostic Diagnostic,
        ImmutableArray<Diagnostic> CompilerDiagnostics,
        AnalyzerOptions AnalyzerOptions);
}

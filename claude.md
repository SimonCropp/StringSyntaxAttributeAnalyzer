# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The repo has two separate solutions that must be run in order — `src/` produces the nuget, `IntegrationTests/` consumes it.

```bash
# Primary workflow: build src/ in Release, which also produces the nupkg in ../nugets/
dotnet build src/StringSyntaxAttributeAnalyzer.slnx -c Release
dotnet run --project src/Tests -c Release                         # analyzer + codefix unit tests
dotnet run --project IntegrationTests/IntegrationTests -c Release # tests that consume the nupkg

# Run a subset of tests (TUnit tree-node filter: /assembly/namespace/class/test)
dotnet run --project src/Tests -c Release -- --treenode-filter "/*/*/*/MultiDeclaratorField*"
dotnet run --project src/Tests -c Release -- --treenode-filter "/*/*/MessageTests/*"

# Code coverage
dotnet run --project src/Tests -c Release -- --coverage --coverage-output-format cobertura

# Repack only (after any analyzer/codefix edit that should reach IntegrationTests)
rm nugets/*.nupkg
dotnet build src/StringSyntaxAttributeAnalyzer.slnx -c Release
```

Tests are **TUnit** on Microsoft.Testing.Platform (MTP). Both test projects are `OutputType=Exe` with a `TUnit` package reference — TUnit supplies the MTP entry point itself, so there is no runner-enabling property to set — and `global.json` pins `"test": { "runner": "Microsoft.Testing.Platform" }`. `dotnet test` is not used on the .NET 10 SDK; run the test project directly and pass MTP arguments after `--`.

Filtering is `--treenode-filter`, **not** the VSTest/NUnit `--filter-method` or `--filter` — those are rejected and TUnit dumps its help instead. The filter is a `/`-separated path (`/assembly/namespace/class/test`) with `*` wildcards in any segment. Only `Samples.cs` declares a namespace, so the test classes sit in the global namespace and that segment has to be `*` — spelling out `StringSyntaxAttributeAnalyzer.Tests` (the `RootNamespace`) matches zero tests. Quote the pattern so the shell doesn't try to expand it as a path.

Coverage output lands in `TestResults/` via `Microsoft.Testing.Extensions.CodeCoverage`.

The `dotnet pack` command alone is fragile here: `ProjectDefaults` only sets `GeneratePackageOnBuild=true` when `IsPackageProject=true` *and* `Configuration=Release`. Packaging is a side-effect of a Release build, not a separate step — match the CI flow in `src/appveyor.yml` (build src Release → build IntegrationTests Release → test both `--no-build --no-restore`).

## Architecture

### Three projects, one nuget

- `src/StringSyntaxAttributeAnalyzer/` — the `MismatchAnalyzer` (DiagnosticAnalyzer, netstandard2.0)
- `src/StringSyntaxAttributeAnalyzer.CodeFixes/` — three `CodeFixProvider`s (netstandard2.0): `AddStringSyntaxCodeFixProvider` (SSA002/003/005/007/009), `ReplaceUnionWithStringSyntaxCodeFixProvider` (SSA006) and `RemoveRedundantConventionCodeFixProvider` (SSA008), plus shared helpers (`AttributeHost`, `AttributeNodeBuilder`, `ShortcutReplacer`, `HostDescription`)
- Both DLLs are packed into a **single** nupkg under `analyzers/dotnet/cs/` via a `PackAnalyzer` target on the analyzer csproj. The analyzer csproj also owns a `BuildCodeFixes` target (`BeforeTargets="Build"`) that explicitly invokes MSBuild on the codefix project — this is how codefix gets built without a ProjectReference.

**The codefix does NOT ProjectReference the analyzer.** An earlier attempt caused a build cycle (analyzer's pack invoked codefix build → codefix referenced analyzer → analyzer rebuild → cycle). Every string that crosses the boundary is therefore declared twice — once in `Rules`, once in the codefix that reads it — and has to be kept in sync by hand. Nothing catches a mismatch at compile time; the symptom is a fix that silently stops being offered. The full contract:

- **Property-bag keys** — `"StringSyntaxValue"` (`Rules.ValueKey`, re-declared in `AddStringSyntaxCodeFixProvider`, `ReplaceUnionWithStringSyntaxCodeFixProvider` and `ShortcutReplacer`) and `"ConventionTarget"` (`Rules.ConventionTargetKey` ↔ `RemoveRedundantConventionCodeFixProvider`).
- **Rule IDs** — the seven that have a fix: SSA002, SSA003, SSA005, SSA007, SSA009 in `AddStringSyntaxCodeFixProvider`; SSA006 in `ReplaceUnionWithStringSyntaxCodeFixProvider`; SSA008 in `RemoveRedundantConventionCodeFixProvider`.
- **Property-bag values** — SSA008's `ConventionTarget` is the bare literal `"Attribute"` (delete the annotation) or `"LanguageComment"` (strip the trivia), written in `MismatchAnalyzer` and compared in `RemoveRedundantConventionCodeFixProvider`.
- **The `|` separator** — `Rules.CreateFixable` joins a multi-value set with `|` (`SyntaxValueMatcher.FormatValues`) and the codefix splits on `|` to recover the options. This is a machine-read separator only: messages render value sets through `Rules.FormatAttribute` instead, which is why a union reads as `[UnionSyntax("Json", "Xml")]` and never as `Json|Xml`.

### Analyzer design

Everything is registered inside one `RegisterCompilationStartAction`, so the per-compilation state (`SyntaxTypes`, `NamespaceSuppression`, `NameConventionsOption`, `LinqFlow`, and the available shortcut attributes) is built once and shared:

- `RegisterOperationAction` × 6 — `Argument`, `SimpleAssignment`, `PropertyInitializer`, `FieldInitializer` (the flow rules SSA001/002/003), `BinaryOperator` (the equality rules SSA004/005), and `Loop` (binds `foreach` variables to their collection's element tags; raises nothing itself).
- `RegisterSymbolAction` × 3 over `Parameter`/`Property`/`Field` — SSA006, SSA007 (also `Method`; registered only when the compilation actually has shortcut attributes) and SSA008.
- `RegisterSyntaxNodeAction` on `LocalDeclarationStatement` — SSA008 for locals, which `RegisterSymbolAction` never visits and whose `//language=` annotation lives in trivia rather than on the symbol.
- `RegisterOperationBlockAction` — SSA009, which decides once per member over every return (expression bodies included).

`StringSyntaxAttribute` has `AttributeUsage(Field | Parameter | Property)`, so `[return: StringSyntax]` is a compile error and cannot exist in legal code. That is the reason the package generates its own `ReturnSyntaxAttribute` (targetable at `Method | Delegate`), and the reason SSA009 reaches returns through an operation-block action rather than a return-statement branch on the flow rules.

The pivotal design decision is **`SyntaxState`** — four states, not a bool:

- `Present` — an annotation (or a `//language=` comment, or an opted-in name convention) supplies a value set.
- `NotPresent` — no annotation, **and** there is a declaration the codefix could attach one to.
- `Unknown` — nothing to read and nowhere to attach a fix.
- `Any` — the explicit `[StringSyntax("*")]` wildcard. Authored intent, deliberately *not* modelled as `Present` with value `"*"`: a `NotPresent` source flowing into an `Any` target would otherwise fire SSA002. Like `Unknown` it suppresses SSA001–SSA005 on its side.

The line between `Unknown` and `NotPresent` is **"could a fix attach here?"**, not "did a symbol resolve?" — `NotPresent` is a promise that SSA002/SSA003/SSA005 have a fix site, so anything unattributable has to be `Unknown` or the analyzer produces warnings nobody can action. Consequences worth knowing before changing `GetSymbol` / `GetSyntax`:

- **Invocations are `NotPresent`**, resolving to `TargetMethod`, because `[ReturnSyntax]` gives them a fix site. An unannotated method flowing into an annotated slot fires SSA002. The exception is a method whose original definition returns a type parameter — `[ReturnSyntax]` there would apply to every substitution, not the `string` one, so `GetSymbol` returns null and the source is `Unknown`.
- **Locals are `NotPresent`** when they are declared by a `LocalDeclarationStatement`, since the codefix can insert `// language=<token>` above it. Pattern and designation locals (`out var x`, `is string s`, `foreach` variables) have no such declaration, so `CanHostLanguageComment` sends them to `Unknown`.
- **Anonymous-type property reads** are `Unknown` — the members are compiler-synthesised and can host no attribute. A `//language=` comment on the originating member initializer is the way to tag one, resolved separately in `GetSourceInfo`.
- **A member typed as a single-`T` enumerable** has its `Present` downgraded to `Unknown` by `SuppressCollectionTag`: an annotation there is an *element* tag, meaningless in a scalar slot.

Literals, interpolated strings, concatenations, `await` and other compound expressions resolve to no symbol at all and stay `Unknown`, which is what keeps every `"foo"` passed to a `[StringSyntax]` parameter quiet.

### Diagnostic messages are a pinned contract

`Rules.cs` owns every descriptor and one `Report` method per rule, so a rule's message arguments, additional-location layout and property bag are defined once, next to the descriptor. The analyzer decides *whether* to fire; `Rules` only builds and raises.

Message text is treated as API, not prose. A message is the only part of a diagnostic that survives into build output, so it has to carry enough to act on without an IDE: both declarations named, the attribute to write spelled out, and the declaration's line (`Rules.Site` renders ` (line 12)` in-file, ` (path:12)` cross-file). Two consequences when editing:

- **`src/Tests/MessageTests.cs` pins the exact text of every rule.** Any wording change fails it by design — update the expectations deliberately, and keep the union cases (`[UnionSyntax("Json", "Xml")]`) and the `[ReturnSyntax]`-for-methods cases, which are what the pins exist to protect.
- **Each descriptor carries `description` and a `helpLinkUri` into `docs/SSA00x.md`.** Adding a rule means adding its page; changing a rule's behaviour means updating the "When it does not fire" list on that page, which is where the suppression rules are documented for consumers.

`Rules.FormatAttribute` picks the attribute name by host kind — `[ReturnSyntax]` for a method, `[UnionSyntax]` for a multi-value set, `[StringSyntax]` otherwise — mirroring what the codefix writes, so a message promises what an applied fix actually does.

### Analyzer → codefix bridge

For SSA002/SSA003, `Rules.CreateFixable` attaches:

- `Properties["StringSyntaxValue"]` — the format value (e.g. `"Regex"`) to add
- `AdditionalLocations[0]` — the **declaration** of the symbol to fix, resolved from `ISymbol.DeclaringSyntaxReferences.FirstOrDefault()`

When the symbol has no `DeclaringSyntaxReferences` (metadata-only), `AdditionalLocations` is empty and the codefix declines to register. The codefix also peeks at the declaration in `RegisterCodeFixesAsync` (via `declarationTree.GetRootAsync`) and skips registration if `AttributeHost.Find` returns null — otherwise the IDE would show a fix that does nothing when applied (multi-declarator field case).

`AttributeHost.Find` handles the `IFieldSymbol` wrinkle: `DeclaringSyntaxReferences` points at `VariableDeclaratorSyntax`, but attribute lists live on the enclosing `FieldDeclarationSyntax` (which applies the attribute to *all* declarators). The codefix walks up from the declarator and refuses when `Variables.Count > 1`.

SSA001 has no codefix by design — picking which side to edit requires human judgement.

### Attribute insertion

The codefix inserts `global::System.Diagnostics.CodeAnalysis.StringSyntax(...)` with `Simplifier.Annotation`, then pipes the document through `ImportAdder.AddImportsAsync` → `Simplifier.ReduceAsync` → `Formatter.FormatAsync`. This produces `[StringSyntax("X")]` when the namespace is imported, the fully-qualified form otherwise — consumer choice. The literal value (not `StringSyntaxAttribute.Regex`) matches what the analyzer reads back via `ConstructorArguments[0].Value as string`, so round-trip is trivial.

### IntegrationTests

`IntegrationTests/` is a parallel tree with its own `nuget.config` pointing at `../nugets` and its own `Directory.Packages.props` pinning `StringSyntaxAttributeAnalyzer` to `$(Version)` (imported from `src/Directory.Build.props`). The tests consume the **packed nupkg**, not the project output — this is what catches packaging bugs that the src/ unit tests can't.

- `ConsumeTests.MatchingStringSyntaxBuildsClean` — `WarningsAsErrors=SSA001;SSA002;SSA003` in the csproj, so any analyzer false-positive fails the build.
- `CodeFixConsumeTests.PackagedCodeFixProvider_*` — loads both DLLs from `~/.nuget/packages/stringsyntaxattributeanalyzer/<version>/analyzers/dotnet/cs/` via `Assembly.LoadFrom`, then instantiates and applies the codefix against an `AdhocWorkspace`. The version comes from an `AssemblyMetadata` attribute injected by the csproj (pinned to `$(Version)`) — scanning the package folder by name picks stale cached versions (lexical sort puts `1.0.0` above `0.1.1`).

### ProjectDefaults

The `ProjectDefaults` nuget (Simon's shared conventions) is referenced by every project. It sets `Nullable=enable`, `ImplicitUsings=enable`, and — critically — controls packing via `IsPackageProject` + Release. It also expects `$(SolutionDir)icon.png` and `$(SolutionDir)key.snk`, neither of which exist here: the analyzer csproj sets `SignAssembly=false`, and a `ClearPackageIcon` target (`BeforeTargets="GenerateNuspec"`) blanks `PackageIcon` to avoid NU5046.

### Readme and snippets

`readme.md` uses [mdsnippets](https://github.com/SimonCropp/MarkdownSnippets) — `<!-- snippet: Name -->` ... `<!-- endSnippet -->` blocks get filled from `#region Name` blocks in `src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs`. Running `dotnet build` on the tests project refreshes the readme (the test csproj references `MarkdownSnippets.MsBuild`). To add a snippet to the readme, add a matching `#region` to `Samples.cs`.

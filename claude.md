# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Commands

The repo has two separate solutions that must be run in order — `src/` produces the nuget, `IntegrationTests/` consumes it.

Tests use TUnit on top of Microsoft.Testing.Platform (MTP) — the MTP `dotnet test` runner is opted-in via `"test": { "runner": "Microsoft.Testing.Platform" }` in each solution's `global.json`. Solutions must be passed to `dotnet test` with `--solution`, not positionally.

```bash
# Primary workflow: build src/ in Release, which also produces the nupkg in ../nugets/
dotnet build src/StringSyntaxAttributeAnalyzer.slnx -c Release
dotnet test --solution src/StringSyntaxAttributeAnalyzer.slnx -c Release          # 23 analyzer + codefix unit tests
dotnet test --solution IntegrationTests/IntegrationTests.slnx -c Release          # 3 tests that consume the nupkg

# Run a single TUnit test (TUnit supports --treenode-filter and --filter-method)
dotnet test --solution src/StringSyntaxAttributeAnalyzer.slnx -c Release -- --filter-method "*MultiDeclaratorField*"

# Repack only (after any analyzer/codefix edit that should reach IntegrationTests)
rm nugets/*.nupkg
dotnet build src/StringSyntaxAttributeAnalyzer.slnx -c Release
```

The `dotnet pack` command alone is fragile here: `ProjectDefaults` only sets `GeneratePackageOnBuild=true` when `IsPackageProject=true` *and* `Configuration=Release`. Packaging is a side-effect of a Release build, not a separate step — match the CI flow in `src/appveyor.yml` (build src Release → build IntegrationTests Release → test both `--no-build --no-restore`).

## Architecture

### Three projects, one nuget

- `src/StringSyntaxAttributeAnalyzer/` — the `MismatchAnalyzer` (DiagnosticAnalyzer, netstandard2.0)
- `src/StringSyntaxAttributeAnalyzer.CodeFixes/` — the `AddStringSyntaxCodeFixProvider` (netstandard2.0)
- Both DLLs are packed into a **single** nupkg under `analyzers/dotnet/cs/` via a `PackAnalyzer` target on the analyzer csproj. The analyzer csproj also owns a `BuildCodeFixes` target (`BeforeTargets="Build"`) that explicitly invokes MSBuild on the codefix project — this is how codefix gets built without a ProjectReference.

**The codefix does NOT ProjectReference the analyzer.** An earlier attempt caused a build cycle (analyzer's pack invoked codefix build → codefix referenced analyzer → analyzer rebuild → cycle). The three strings `"StringSyntaxValue"`, `"SSA002"`, `"SSA003"` are deliberately duplicated between `MismatchAnalyzer` and `AddStringSyntaxCodeFixProvider` to keep the projects decoupled. Keep them in sync when editing.

### Analyzer design

`MismatchAnalyzer` uses `RegisterOperationAction` on four kinds: `Argument`, `SimpleAssignment`, `PropertyInitializer`, `FieldInitializer`. No return-statement branch — `StringSyntaxAttribute` has `AttributeUsage(Field | Parameter | Property)`, so `[return: StringSyntax]` is a compile error and cannot exist in legal code.

The pivotal design decision is the **tri-state `SyntaxState`** (`Unknown` / `NotPresent` / `Present`) — not a bool. A source is `Unknown` when it resolves to a string literal, local variable, invocation result, concatenation, `await`, or any other non-resolvable symbol; `NotPresent` only when a resolvable `ISymbol` (property / field / parameter) exists but lacks the attribute. `Unknown` suppresses all three diagnostics. This is what prevents noise on every `"foo"` or `.ToString()` passed to a `[StringSyntax]` parameter.

`IInvocationOperation` maps to `Unknown` (not `NotPresent`), for the same reason — method return values cannot carry the attribute, so treating them as "no attribute present" would fire SSA002 on every method-returned string flowing into a formatted slot.

### Analyzer → codefix bridge

For SSA002/SSA003, `MismatchAnalyzer.CreateFixableDiagnostic` attaches:

- `Properties["StringSyntaxValue"]` — the format value (e.g. `"Regex"`) to add
- `AdditionalLocations[0]` — the **declaration** of the symbol to fix, resolved from `ISymbol.DeclaringSyntaxReferences.FirstOrDefault()`

When the symbol has no `DeclaringSyntaxReferences` (metadata-only), `AdditionalLocations` is empty and the codefix declines to register. The codefix also peeks at the declaration in `RegisterCodeFixesAsync` (via `declarationTree.GetRootAsync`) and skips registration if `FindAttributeHost` returns null — otherwise the IDE would show a fix that does nothing when applied (multi-declarator field case).

`FindAttributeHost` handles the `IFieldSymbol` wrinkle: `DeclaringSyntaxReferences` points at `VariableDeclaratorSyntax`, but attribute lists live on the enclosing `FieldDeclarationSyntax` (which applies the attribute to *all* declarators). The codefix walks up from the declarator and refuses when `Variables.Count > 1`.

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

`readme.md` uses [mdsnippets](https://github.com/SimonCropp/MarkdownSnippets) — `<!-- snippet: Name -->` ... `<!-- endSnippet -->` blocks get filled from `#region Name` blocks in `src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs`. Running `dotnet build` on the tests project refreshes the readme (the test csproj references `MarkdownSnippets.MsBuild`). If you add a snippet to the readme, add a matching `#region` to `Samples.cs`.

# <img src="/src/icon.png" height="30px"> StringSyntaxAttributeAnalyzer

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/StringSyntaxAttributeAnalyzer)](https://ci.appveyor.com/project/SimonCropp/StringSyntaxAttributeAnalyzer)
[![NuGet Status](https://img.shields.io/nuget/v/StringSyntaxAttributeAnalyzer.svg?label=StringSyntaxAttributeAnalyzer)](https://www.nuget.org/packages/StringSyntaxAttributeAnalyzer/)

Roslyn analyzer that reports mismatches between [`StringSyntaxAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.stringsyntaxattribute) values when a string flows from one annotated member to another.

**See [Milestones](../../milestones?state=closed) for release notes.**


## Diagnostics

| ID     | Severity | Code fix | Description                                                        |
|--------|----------|----------|--------------------------------------------------------------------|
| SSA001 | Warning  | —        | Format mismatch — both sides have `StringSyntax` but values differ |
| SSA002 | Warning  | Yes      | Source has no `StringSyntax` while the target requires one         |
| SSA003 | Warning  | Yes      | Source has `StringSyntax` while the target has none                |
| SSA004 | Warning  | —        | Equality comparison between mismatched `StringSyntax` values       |
| SSA005 | Warning  | Yes      | Equality comparison where only one side has `StringSyntax`         |
| SSA006 | Warning  | Yes      | `[UnionSyntax("x")]` with a single option — should be `[StringSyntax("x")]` |

SSA002, SSA003, and SSA005 ship a code fix that adds `[StringSyntax("<value>")]` to the declaration that lacks one. SSA006 rewrites the attribute in place. SSA001 and SSA004 have no fix because both sides already have attributes and picking which side is wrong requires human judgement.


## Code fix output — named constants vs. string literals

When the value matches one of the constants on the source-generated `Syntax` class (`Regex`, `Json`, `Email`, `Uri`, `Html`, `Xml`, `Markdown`, `Yaml`, `Csv`, `Sql`, `Text`, `CompositeFormat`, `DateOnlyFormat`, `DateTimeFormat`, `EnumFormat`, `GuidFormat`, `NumericFormat`, `TimeOnlyFormat`, `TimeSpanFormat`), the code fix emits the named constant rather than a raw string:

```cs
// Value resolves to a known constant — emitted as Syntax.Regex
[Syntax(Syntax.Regex)]
public string Pattern { get; set; }

// Value is project-specific — emitted as a literal
[Syntax("custom-format")]
public string Marker { get; set; }
```

Matching is case-sensitive against the constant name (`"Regex"` matches, `"regex"` does not). The short `Syntax`/`ReturnSyntax` attribute form and the `Syntax.X` constant reference both require the generator's global usings to be in scope — projects that opt out (see the next section) receive the long form with a string literal (`[StringSyntax("Regex")]`) so the result compiles without further imports.


## Opting out of the global usings

The source generator emits three `global using` directives so `[StringSyntax]`, `[UnionSyntax]`, and `Syntax.*` are friction-free to reference without per-file imports:

```csharp
global using System.Diagnostics.CodeAnalysis;
global using StringSyntaxAttributeAnalyzer;
global using SyntaxAttribute = System.Diagnostics.CodeAnalysis.StringSyntaxAttribute;
```

In strict codebases that prefer explicit imports, set this MSBuild property:

```xml
<PropertyGroup>
  <StringSyntaxAnalyzer_EmitGlobalUsings>false</StringSyntaxAnalyzer_EmitGlobalUsings>
</PropertyGroup>
```

With the property set to `false`, the `Syntax.Globals.g.cs` file is not emitted. `UnionSyntaxAttribute` and `Syntax` are still generated (they are required for the feature to exist), but are no longer globally in scope — add `using System.Diagnostics.CodeAnalysis;` and `using StringSyntaxAttributeAnalyzer;` in the files that reference them.

The property is wired via a `build/StringSyntaxAttributeAnalyzer.props` file that ships in the package and is imported automatically by NuGet.


## Shortcut attributes (opt-in)

For a more concise syntax, the package can emit one attribute per known constant — `[Html]` as a shortcut for `[StringSyntax(Syntax.Html)]`, `[Regex]` for `[StringSyntax(Syntax.Regex)]`, and so on for every entry on the `Syntax` class (`Json`, `Xml`, `Sql`, `Yaml`, `Csv`, `Markdown`, `Email`, `Uri`, `Text`, and the BCL format names such as `DateTimeFormat`, `NumericFormat`, `CompositeFormat`, etc.).

This feature is **opt-in**:

```xml
<PropertyGroup>
  <StringSyntaxAnalyzer_EmitShortcutAttributes>true</StringSyntaxAnalyzer_EmitShortcutAttributes>
</PropertyGroup>
```

When enabled, the generator emits `Syntax.Shortcuts.g.cs` with one `internal sealed class <Name>Attribute : System.Attribute` per known constant in the `StringSyntaxAttributeAnalyzer` namespace. The SSA002/SSA003/SSA005 codefixes also switch over: instead of offering `[Syntax(Syntax.Html)]`, they offer `[Html]`. A dedicated **SSA007** warning flags existing `[StringSyntax("Html")]` / `[StringSyntax(Syntax.Html)]` attributes and offers a one-click "Replace with `[Html]`" fix — so a whole codebase can be migrated in a single apply-all. Usage is then:

```csharp
public class Messages
{
    [Html]
    public string Body { get; set; } = "";

    public void Render([Regex] string pattern) { }
}
```

### Tradeoffs — read before enabling

These shortcut attributes are **recognized only by this analyzer**. They are independent types, not subclasses of `StringSyntaxAttribute` (which is `sealed` and cannot be inherited). That has two direct consequences:

1. **Other tools see nothing.** The BCL, Roslyn's built-in string-syntax analyzers, Rider's language injection, and any third-party tooling that keys off `[StringSyntax(...)]` will ignore `[Html]`, `[Regex]`, etc. — those members appear unannotated to every consumer except this analyzer. Where `[StringSyntax]` is a standard contract that travels with the metadata, the shortcuts are a dialect local to projects that reference this package.
2. **Cross-assembly surface needs coordination.** Because the attributes are source-generated as `internal` per assembly, a shortcut on a public API in assembly A is still seen by this analyzer in a consuming assembly B — but only if B *also* references this analyzer. A consumer that doesn't will see a bare string with no annotation at all.

**Only recommended when every project in a domain uses this analyzer.** For a self-contained application or a set of libraries under one team's control where the team has standardized on this package, the shortcuts are a concise, readable win. For shipping a public library, a plug-in SDK, or anything consumed by projects outside that boundary, stick with `[StringSyntax(...)]` — it's the standard contract and the shortcut's terseness isn't worth the asymmetric visibility.

If in doubt, leave this off. `[StringSyntax(Syntax.Html)]` already reads well and costs nothing in portability.


## `[UnionSyntax(...)]`

Sometimes a member can legitimately hold any one of several syntaxes — a cell in a report that's either HTML or plain text, a payload field that's JSON or YAML. The package ships `[UnionSyntax("html", "xml")]` for that case.

Two values are *compatible* when their option sets overlap on at least one entry:

| Source                                 | Target                                   | Compatible?     |
|----------------------------------------|------------------------------------------|-----------------|
| `[UnionSyntax("html","xml")]`          | `[UnionSyntax("html","xml")]`            | yes (full)      |
| `[UnionSyntax("html","xml")]`          | `[UnionSyntax("html","js")]`             | yes (`html`)    |
| `[UnionSyntax("html","xml")]`          | `[StringSyntax("xml")]`                  | yes             |
| `[StringSyntax("xml")]`                | `[UnionSyntax("html","xml")]`            | yes             |
| `[UnionSyntax("html","xml")]`          | `[UnionSyntax("json","yaml")]`           | **no** → SSA001 |

A `[UnionSyntax("x")]` with a single option is always a mistake — use `[StringSyntax("x")]`. SSA006 flags it and suggests a fix.


## Analyzed Sites

Diagnostics fire on:

 * Method, constructor, indexer, and delegate arguments
 * Assignment expressions (`=`), including inside object initializers
 * Property and field inline initializers


## Sources the analyzer can resolve

The source of a string is resolved when it is one of:

 * A property reference (`obj.Prop`)
 * A field reference (`obj._field`)
 * A parameter reference (method argument, lambda parameter, etc.)
 * A method invocation (read from `[ReturnSyntax(...)]` on the method — unannotated methods are treated as bare, same as an unattributed property)
 * A local variable declaration (read from `//language=<name>` comments — unannotated locals are treated as bare)

Other sources — string literals, interpolated strings, concatenation, `await`, binary expressions, etc. — are treated as **unknown** and suppress all three diagnostics. This avoids noise on every `"foo"` passed to a `[StringSyntax]` parameter.

Invocations of BCL / framework methods are silenced by the namespace suppression below — `string.Format`, `sb.ToString()`, and `Path.Combine` won't fire SSA002 even though their return values aren't annotated.

Likewise, when the **target** is `object`, `params object?[]`, or a generic type parameter (`T`) without its own `StringSyntax`, the analyzer treats it as a generic value slot and suppresses SSA003/SSA005. That keeps logging calls like `logger.Info("processing {P}", pattern)` quiet — the logger was never going to honour a format contract on `pattern`.

Symmetrically, sources whose declaring type lives in a suppressed namespace (BCL by default) are skipped on the SSA002/SSA005 paths — an unannotated BCL method or property is not something the consumer can add `[StringSyntax]` / `[ReturnSyntax]` to, so surfacing an unfixable warning would be noise. Same config knob as the target side.


## Local variables — `//language=<name>` comments

C# doesn't permit attributes on local variables, so `[StringSyntax]` has nowhere to hang. To describe a local's syntax the analyzer reads the JetBrains / IntelliJ-platform [language injection](https://www.jetbrains.com/help/rider/Language_Injections.html#use-comments) comment convention — the same comment Rider and ReSharper already honour, so adopting it is zero-cost for anyone already using those tools.

```cs
public void Use()
{
    // language=regex
    var pattern = "[a-z]+";
    Consume(pattern); // flows as [StringSyntax("Regex")] — no diagnostic
}
```

### Accepted forms

 * `// language=<name>` or `//language=<name>` on the line preceding the declaration
 * `var x = /*language=<name>*/ "..."` inline before the initializer
 * Optional `prefix=` / `postfix=` follow-on options are ignored (they're renderer hints, irrelevant to syntax identity)
 * Keyword `language` is matched case-insensitively

### Token names

Rider spells regex as `regexp`; the BCL uses `Regex`. The analyzer normalizes `regexp` → `Regex` so `//language=regexp` matches `[StringSyntax(StringSyntaxAttribute.Regex)]` without requiring the user to know the naming history. Other tokens (`json`, `html`, `xml`, `sql`, ...) pass through and match case-insensitively against BCL PascalCase (`Json`, `Html`, `Xml`, ...).

### Unannotated locals fire SSA002

A local flowing into a `[StringSyntax]` target without a `//language=` comment fires **SSA002**, with a code fix that inserts `// language=<token>` above the declaration. Token names follow Rider conventions — `regex` is emitted as `regexp` (so Rider's own highlighting lights up), everything else is lowercased.

Locals that never flow into a `[StringSyntax]` target are untouched — `var name = user.GetName();` on its own produces no diagnostic; only the passthrough into a formatted slot surfaces the warning.


## `[ReturnSyntax(...)]` on methods

`StringSyntaxAttribute` has `AttributeUsage(Field | Parameter | Property)`, so `[return: StringSyntax(...)]` is a compile error and the BCL attribute cannot describe the syntax of a method's return value. Broadening its targets is tracked in [dotnet/runtime#76203](https://github.com/dotnet/runtime/issues/76203); until that ships, the source generator emits `ReturnSyntaxAttribute` (targetable at `Method | Delegate`) as a bridge so invocation results can participate in format analysis.

### Example

```cs
[ReturnSyntax(StringSyntaxAttribute.Regex)]
public string GetPattern() => "[a-z]+";

public void ConsumePattern([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

public void Use() => ConsumePattern(GetPattern()); // no diagnostic — invocation is Present
```

With the annotation, calls to `GetPattern()` are treated as `[StringSyntax("Regex")]`-attributed at every call site. A call passing the result into a mismatched target fires SSA001; passing it into a bare `string` parameter fires SSA003; passing it into the matching target is silent.

### Unannotated methods fire SSA002

An unannotated method flowing into a `[StringSyntax]` target fires **SSA002**, with a code fix that adds `[ReturnSyntax("...")]` to the method declaration. This mirrors the behaviour for an unannotated property or field source. BCL methods are silenced automatically by the namespace suppression — a call to `string.Format` or `sb.ToString()` does not produce a diagnostic, because the consumer can't annotate those methods anyway. Third-party libraries outside `System*` / `Microsoft*` can be added to `stringsyntax.suppressed_target_namespaces` when needed.

### Planned retirement

When [dotnet/runtime#76203](https://github.com/dotnet/runtime/issues/76203) is resolved and `StringSyntaxAttribute` targets methods/return values directly, `ReturnSyntaxAttribute` becomes redundant. The analyzer will start reading the BCL attribute off method return values, and this package-emitted attribute will be removed — consumers should be able to migrate with a find-and-replace.


## Record primary-constructor parameters

When a record is declared with a primary constructor, `[StringSyntax(...)]` written on a parameter applies to both the parameter and the auto-generated property. The C# compiler leaves the attribute physically on the parameter (its default target), so reading `record.Member` would otherwise look unattributed. The analyzer bridges this gap: a property synthesized from a primary-constructor parameter inherits the parameter's `[StringSyntax]` / `[UnionSyntax]` for analysis purposes.

<!-- snippet: RecordPrimaryCtorParameter -->
<a id='snippet-RecordPrimaryCtorParameter'></a>
```cs
public record PatternRecord([StringSyntax(StringSyntaxAttribute.Regex)] string Pattern);

public void RecordCall(PatternRecord record) =>
    ConsumeRegexStrict(record.Pattern); // no diagnostic — attribute flows to property
```
<sup><a href='/src/Tests/Samples.cs#L81-L88' title='Snippet source file'>snippet source</a> | <a href='#snippet-RecordPrimaryCtorParameter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

An explicit `[property: StringSyntax(...)]` on the property still wins — if both targets are attributed, the property's own attribute is used.


## Suppressed target namespaces

SSA003 and SSA005 point at the *target* symbol's declaration as the fix site. When the target lives in a namespace the consumer can't edit (the BCL, third-party packages), the warning is unfixable noise. The analyzer skips those diagnostics when the target's containing namespace matches one of the configured patterns.

**Default**: `System*,Microsoft*` — covers `System.Console`, `System.IO.File.WriteAllText`, `Microsoft.Extensions.Logging.ILogger`, etc.

**Override** via `.editorconfig`:

```ini
stringsyntax.suppressed_target_namespaces = System*,Microsoft*,MyCompany.Legacy*
```

Patterns support a trailing `*` (prefix match) or an exact match. Empty config means "no namespaces suppressed".


## Usage


### SSA001 — Format mismatch

A source with one `StringSyntax` value assigned to a target with a different `StringSyntax` value.

<!-- snippet: FormatMismatch -->
<a id='snippet-FormatMismatch'></a>
```cs
public class MismatchHolder
{
    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    public string Format { get; set; } = "";
}

public void ConsumeRegex([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
{
}

public void FormatMismatchCall(MismatchHolder holder) =>
    ConsumeRegex(holder.Format); // SSA001
```
<sup><a href='/src/Tests/Samples.cs#L10-L25' title='Snippet source file'>snippet source</a> | <a href='#snippet-FormatMismatch' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### SSA002 — Missing source format

A source without `StringSyntax` assigned to a target that requires one.

<!-- snippet: MissingSourceFormat -->
<a id='snippet-MissingSourceFormat'></a>
```cs
public class UntypedHolder
{
    public string Value { get; set; } = "";
}

public void ConsumeRegexStrict([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
{
}

public void MissingSourceCall(UntypedHolder holder) =>
    ConsumeRegexStrict(holder.Value); // SSA002
```
<sup><a href='/src/Tests/Samples.cs#L27-L41' title='Snippet source file'>snippet source</a> | <a href='#snippet-MissingSourceFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SSA002 is the strongest signal — it catches unvalidated strings flowing into format-typed slots. It is the most likely of the three to surface real bugs.


### SSA003 — Dropped format

A source with `StringSyntax` assigned to a target without one.

<!-- snippet: DroppedFormat -->
<a id='snippet-DroppedFormat'></a>
```cs
public class RegexHolder
{
    [StringSyntax(StringSyntaxAttribute.Regex)]
    public string Pattern { get; set; } = "";
}

public void ConsumeAnyString(string value)
{
}

public void DroppedFormatCall(RegexHolder holder) =>
    ConsumeAnyString(holder.Pattern); // SSA003
```
<sup><a href='/src/Tests/Samples.cs#L43-L58' title='Snippet source file'>snippet source</a> | <a href='#snippet-DroppedFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SSA003 is the weakest signal — discarding format metadata is usually benign. Consider disabling it in projects where it produces noise.


### Matching formats — no diagnostic

<!-- snippet: MatchingFormat -->
<a id='snippet-MatchingFormat'></a>
```cs
public void MatchingCall(RegexHolder holder) =>
    ConsumeRegexStrict(holder.Pattern); // no diagnostic
```
<sup><a href='/src/Tests/Samples.cs#L60-L65' title='Snippet source file'>snippet source</a> | <a href='#snippet-MatchingFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Unknown sources — no diagnostic

Expressions without a resolvable symbol flow as `Unknown` and suppress SSA002.

#### String literals

A literal has no backing symbol to inspect, so the analyzer cannot claim the attribute is missing.

<!-- snippet: LiteralSource -->
<a id='snippet-LiteralSource'></a>
```cs
public void LiteralCall() =>
    ConsumeRegexStrict("[a-z]+"); // no diagnostic — literal is Unknown
```
<sup><a href='/src/Tests/Samples.cs#L67-L72' title='Snippet source file'>snippet source</a> | <a href='#snippet-LiteralSource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Other non-resolvable expressions

Concatenations, interpolations, and `await` expressions don't reduce to a single symbol, so they're Unknown too.

<!-- snippet: OtherUnknownSource -->
<a id='snippet-OtherUnknownSource'></a>
```cs
public void ConcatCall(string suffix) =>
    ConsumeRegexStrict("[a-z]" + suffix); // no diagnostic — concatenation is Unknown
```
<sup><a href='/src/Tests/Samples.cs#L74-L79' title='Snippet source file'>snippet source</a> | <a href='#snippet-OtherUnknownSource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Icon

[Pattern](https://thenounproject.com/icon/pattern-8046712/) from [The Noun Project](https://thenounproject.com).

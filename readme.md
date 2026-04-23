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
| SSA008 | Warning  | Yes      | Annotation is redundant — the symbol's name already matches a known convention (opt-in) |

SSA002, SSA003, and SSA005 ship a code fix that adds `[StringSyntax("<value>")]` to the declaration that lacks one. SSA006 rewrites the attribute in place. SSA008 removes the redundant attribute or `//language=` comment. SSA001 and SSA004 have no fix because both sides already have attributes and picking which side is wrong requires human judgement.


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

When enabled, the generator emits `Syntax.Shortcuts.g.cs` with one `internal sealed class <Name>Attribute : System.Attribute` per known constant in the `StringSyntaxAttributeAnalyzer` namespace. Their `AttributeUsage` covers `Field | Parameter | Property | ReturnValue`, so `[return: Json]` on a method is legal and is read by the analyzer as the equivalent of `[ReturnSyntax(Syntax.Json)]`. The SSA002/SSA003/SSA005 codefixes also switch over: instead of offering `[Syntax(Syntax.Html)]`, they offer `[Html]`. A dedicated **SSA007** warning flags existing `[StringSyntax("Html")]` / `[StringSyntax(Syntax.Html)]` / `[ReturnSyntax(Syntax.Json)]` attributes and offers a one-click "Replace with `[Html]`" (or "Replace with `[return: Json]`" for methods) fix — so a whole codebase can be migrated in a single apply-all. Usage is then:

```csharp
public class Messages
{
    [Html]
    public string Body { get; set; } = "";

    public void Render([Regex] string pattern) { }

    [return: Json]
    public string Build() => "{}";
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
 * Pipe-delimited unions — `//language=json|csv` — express the same shape as `[UnionSyntax("Json", "Csv")]` on a property/field/parameter. A union-tagged local flows into a union-typed target without SSA002. (Rider's own injection only honours the first segment; the analyzer accepts the whole set.)
 * Optional `prefix=` / `postfix=` follow-on options are ignored (they're renderer hints, irrelevant to syntax identity)
 * Keyword `language` is matched case-insensitively

### Token names

Rider spells regex as `regexp`; the BCL uses `Regex`. The analyzer normalizes `regexp` → `Regex` so `//language=regexp` matches `[StringSyntax(StringSyntaxAttribute.Regex)]` without requiring the user to know the naming history. Other tokens (`json`, `html`, `xml`, `sql`, ...) pass through and match case-insensitively against BCL PascalCase (`Json`, `Html`, `Xml`, ...).

### Unannotated locals fire SSA002

A local flowing into a `[StringSyntax]` target without a `//language=` comment fires **SSA002**, with a code fix that inserts `// language=<token>` above the declaration. Token names follow Rider conventions — `regex` is emitted as `regexp` (so Rider's own highlighting lights up), everything else is lowercased.

When the target is a union (`[UnionSyntax("Json", "Csv")]`), the code fix surfaces one option per member (`// language=json`, `// language=csv`) plus an additional pipe-delimited union option (`// language=json|csv`) that covers the full set in a single comment.

Locals that never flow into a `[StringSyntax]` target are untouched — `var name = user.GetName();` on its own produces no diagnostic; only the passthrough into a formatted slot surfaces the warning.


## Anonymous-type projections — `//language=<name>` on the member

Anonymous-type members are compiler-synthesised and can't host `[StringSyntax]`, so projections like `.Select(_ => new { _.Tagged })` silently drop the tag by default — passing a tagged source into a `new { … }` shape, or reading the anon member back out, produces no diagnostic.

Authors who want to preserve the tag can annotate the anon member's initializer with the same `//language=<name>` comment used for locals. The analyzer then treats that member as `Present(value)` on both sides of the projection: the write site is checked against the source's tag, and reads of the anon instance (traced through local declarations, element-returning LINQ, and `Select` projections) surface the comment's value as the effective syntax.

<!-- snippet: AnonymousProjectionLanguageComment -->
<a id='snippet-AnonymousProjectionLanguageComment'></a>
```cs
public class DataRow
{
    [StringSyntax("Json")]
    public string Payload { get; set; } = "";
}

public class AnonProjectionReader
{
    public IEnumerable<DataRow> Rows { get; set; } = [];

    public void ConsumeJson([StringSyntax("Json")] string value) { }

    public void ConsumeRegex([StringSyntax("Regex")] string value) { }

    public void Go()
    {
        // Anonymous-type members can't host [StringSyntax]. Annotate the
        // member initializer with `//language=<name>` to opt the projection
        // into validation — the tag flows both ways through the anon instance.
        var row = Rows.Select(_ => new
        {
            //language=Json
            _.Payload
        }).First();

        // No diagnostic — row.Payload carries the annotated Json tag.
        ConsumeJson(row.Payload);

        // SSA001 — Json flowing into a Regex-tagged parameter.
        ConsumeRegex(row.Payload);
    }
}
```
<sup><a href='/src/Tests/Samples.cs#L200-L235' title='Snippet source file'>snippet source</a> | <a href='#snippet-AnonymousProjectionLanguageComment' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

The read-side trace follows the instance back through: direct `new { … }`, a local whose initializer evaluates to the anon, element-returning LINQ (`.First()` / `.Single()` / their `*Async` counterparts), element-preserving LINQ (`Where`, `Take`, …), and `Select(_ => new { … })`. Anon instances reached via method returns, parameters, or other unresolved expression shapes fall back to the silent default.


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

When shortcut attributes are opted in (see [Shortcut attributes](#shortcut-attributes-opt-in)), the single-value form `[ReturnSyntax(Syntax.Regex)]` can be written more concisely as `[return: Regex]`. SSA007 offers a codefix to migrate existing `[ReturnSyntax(...)]` declarations.

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
<sup><a href='/src/Tests/Samples.cs#L82-L89' title='Snippet source file'>snippet source</a> | <a href='#snippet-RecordPrimaryCtorParameter' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

An explicit `[property: StringSyntax(...)]` on the property still wins — if both targets are attributed, the property's own attribute is used.


## Tagged collections

A `[StringSyntax(...)]` or `[UnionSyntax(...)]` on a collection-typed member describes the elements inside the collection, not the collection itself. The analyzer threads those element syntax values through LINQ queries, `foreach` loops, and user-defined extensions — so flow works without attributes on every lambda parameter (which are illegal inside `IQueryable` expression trees anyway, per CS8972).

The collection must be a **single-T enumerable**: an array, or a type that implements exactly one `IEnumerable<T>` construction. That covers `T[]`, `IEnumerable<T>`, `IReadOnlyList<T>`, `List<T>`, `HashSet<T>`, `IQueryable<T>`, and the various immutable/concurrent flavours.

<!-- snippet: TaggedCollectionLinqLambda -->
<a id='snippet-TaggedCollectionLinqLambda'></a>
```cs
public class HtmlBodies
{
    // [StringSyntax] on a single-T collection describes its elements. The syntax
    // flows into any site that extracts an element: lambda parameters, foreach
    // variables, .First() results, and through chains of LINQ-shape element-
    // preserving calls.
    [StringSyntax("Html")]
    public IEnumerable<string> Values { get; set; } = [];
}

public class RegexConsumer
{
    public void Consume([StringSyntax("Regex")] string value) { }

    public void Go(HtmlBodies bodies) =>
        // SSA001 on the argument: `s` inherits "Html" from bodies.Values, which
        // is then passed into a parameter tagged "Regex".
        bodies.Values.Select(_ => { Consume(_); return _; }).ToList();
}
```
<sup><a href='/src/Tests/Samples.cs#L98-L120' title='Snippet source file'>snippet source</a> | <a href='#snippet-TaggedCollectionLinqLambda' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Only **explicit** `[StringSyntax]` / `[UnionSyntax]` / `[ReturnSyntax]` (and generated shortcut attributes) on a collection-typed declaration participate in element-flow. Name-convention inference is not applied to collection-typed members — a `List<string>` happening to be named `Html` would otherwise spuriously acquire a syntax no caller can change.


### LINQ

Syntax values flow through three categories of call, classified by signature rather than by name:

 * **Element-returning** — `First`, `FirstOrDefault`, `Single`, `SingleOrDefault`, `Last`, `LastOrDefault`, `ElementAt`, `ElementAtOrDefault`, `Min`, `Max`, `Aggregate` on `System.Linq.Enumerable`/`Queryable` surface the receiver's element syntax as the result's scalar syntax. The `*Async` counterparts from EF Core (`FirstAsync`, `SingleAsync`, …) are recognised by shape: any element-returning name + `Async` whose return type is `Task<T>` / `ValueTask<T>` over the receiver's element type flows the same way, so `await q.Select(_ => _.Tagged).SingleAsync()` is treated as a tagged scalar.
 * **Element-preserving** — `Where`, `OrderBy` / `OrderByDescending`, `ThenBy` / `ThenByDescending`, `Reverse`, `Take` / `TakeWhile` / `TakeLast`, `Skip` / `SkipWhile` / `SkipLast`, `Distinct` / `DistinctBy`, `Concat`, `Union` / `UnionBy`, `Intersect` / `IntersectBy`, `Except` / `ExceptBy`, `AsEnumerable`, `AsQueryable`, `ToArray`, `ToList`, `ToHashSet`, `Append`, `Prepend` pass the element syntax through unchanged, so chains like `docs.Where(x => x.Length > 0).First()` work.
 * **`Select` / `SelectMany`** transform the element syntax according to the selector:
   * Identity lambda `x => x` keeps the receiver's element syntax.
   * Method group `Select(Converter)` reads `[ReturnSyntax(...)]` / `[return: StringSyntax(...)]` on the target method.
   * Expression-bodied lambda with a tagged body (`Select(x => GetTagged(x))`) adopts the body's resolved syntax.
   * Any other selector shape drops the syntax.

Lambda parameters in those calls inherit the receiver's element syntax without an attribute, which is the mechanism that makes `IQueryable` predicates analyzable.


### `foreach`

The loop variable inherits the collection's element syntax for the body of the loop. Nested `foreach` works the same way — the inner loop sees the inner collection's element syntax.

<!-- snippet: TaggedCollectionForEach -->
<a id='snippet-TaggedCollectionForEach'></a>
```cs
public class HtmlScan
{
    [StringSyntax("Html")]
    public IEnumerable<string> Values { get; set; } = [];

    public void ConsumeRegex([StringSyntax("Regex")] string value) { }

    public void Go()
    {
        foreach (var s in Values)
        {
            // SSA001: `s` carries the Html syntax inherited from the collection.
            ConsumeRegex(s);
        }
    }
}
```
<sup><a href='/src/Tests/Samples.cs#L122-L141' title='Snippet source file'>snippet source</a> | <a href='#snippet-TaggedCollectionForEach' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### User-defined LINQ-shape extensions

An extension method whose first parameter is `IEnumerable<T>` or `T[]`, and whose return is an `IEnumerable<T>` over the same `T`, is treated as element-preserving by shape. MoreLINQ helpers, EF Core `IQueryable` extensions like `.Include`, and project-local paging helpers all propagate element syntax without being on an allowlist.

<!-- snippet: TaggedCollectionUserExtension -->
<a id='snippet-TaggedCollectionUserExtension'></a>
```cs
public static class Paged
{
    // An extension with shape `IEnumerable<T> → IEnumerable<T>` is treated as
    // element-preserving, so element syntax flows through it just like through
    // `Where`, `Take`, and `OrderBy`.
    public static IEnumerable<T> TakePage<T>(this IEnumerable<T> source, int page, int size) =>
        source.Skip(page * size).Take(size);
}

public class PagedReader
{
    [StringSyntax("Html")]
    public IEnumerable<string> Values { get; set; } = [];

    [StringSyntax("Regex")]
    public string Target { get; set; } = "";

    // SSA001 on the assignment: .First() returns an Html-tagged string after
    // passing through the user-defined element-preserving extension.
    public void Copy() => Target = Values.TakePage(0, 10).First();
}
```
<sup><a href='/src/Tests/Samples.cs#L143-L167' title='Snippet source file'>snippet source</a> | <a href='#snippet-TaggedCollectionUserExtension' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Lambda-parameter binding applies to any extension method on `IEnumerable<T>` regardless of its return type — so `Action<T>` callbacks and void-returning helpers flow syntax the same way.

Element-returning inference (`.First()` and friends) stays closed to the `System.Linq.Enumerable`/`Queryable` allowlist — a third-party method named `First` could have different semantics, and the element-returning category depends on the semantics, not the signature.


### Dictionaries, groupings, and key/value streams

`[StringSyntax(...)]` on a dictionary-like member applies to a single *position* inferred from its type arguments:

 * **Exactly one of K / V is `string`** — the tag applies to that side.
 * **Both K and V are `string`** — the tag defaults to the **Value** position. Key-side tagging on this shape is not supported; workaround is to wrap the key in a typed record (`record HtmlId([StringSyntax("Html")] string Value)`) so the two sides are statically distinct.
 * **Neither is `string`** — no position can hold a StringSyntax value; the attribute is silently ignored.

Recognition is broad: `Dictionary<K,V>`, `IDictionary<K,V>`, `IReadOnlyDictionary<K,V>`, any `IEnumerable<KeyValuePair<K,V>>` (query results), `IGrouping<K,T>`, and `IEnumerable<IGrouping<K,T>>` (GroupBy results) all follow the same rule.

<!-- snippet: DictionaryPositional -->
<a id='snippet-DictionaryPositional'></a>
```cs
public class TemplateStore
{
    // Dictionary<K, V> with exactly one string-typed position: the attribute
    // applies to that position. Here V is string, so [StringSyntax("Html")]
    // describes the Value position — dict[k], kv.Value, dict.Values.First(),
    // and foreach-bound kv.Value all carry the Html tag.
    [StringSyntax("Html")]
    public Dictionary<int, string> Bodies { get; set; } = [];

    public void ConsumeRegex([StringSyntax("Regex")] string value) { }

    // SSA001: Bodies[0] is a Value-position read and carries Html.
    public void Go() => ConsumeRegex(Bodies[0]);
}
```
<sup><a href='/src/Tests/Samples.cs#L169-L186' title='Snippet source file'>snippet source</a> | <a href='#snippet-DictionaryPositional' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

Sites that surface the tagged position:

 * **Indexer reads** — `dict[k]` resolves to the Value position.
 * **`foreach (var kv in dict)`** — subsequent `kv.Key` / `kv.Value` reads inside the loop body resolve to the tagged side.
 * **`dict.First()` / `.Single()` / `.Last()` / ...** — the returned `KeyValuePair` remembers its position; reading `.Key` / `.Value` on the result flows the tag.
 * **`dict.Keys` / `dict.Values`** — projected collections flow element syntax through the usual single-T path, so `dict.Values.First()` or `foreach (var v in dict.Values)` works.
 * **`grouping.Key`** (on `IGrouping<K,T>` where K=string) — reads the Key-position tag directly.

### What is not supported

 * **`TryGetValue(k, out var v)`**: the out-var local isn't declared via `LocalDeclarationStatement`, so there's nowhere to attach a binding. Workaround: hoist the lookup into a regular read — `var v = dict[k]` or `dict.GetValueOrDefault(k)`.
 * **`ILookup<K,V>`**: enumerates to `IGrouping<K,V>` — as a container the lookup itself isn't recognised, though individual groupings obtained from it (via `.SelectMany`, indexing, etc.) work normally.
 * **`ValueTuple<T1, T2, ...>`**: C# doesn't support attributing individual tuple fields, so there's no declaration site for a position-specific tag. Use a named record instead.
 * **Key-side tagging of `Dictionary<string,string>`**: the default-to-Value rule covers the dominant case; disambiguating requires either a wrapper type on the key side or a dedicated `[KeyStringSyntax]` attribute, which would be opt-in and require a separate design pass.


## Suppressed target namespaces

SSA003 and SSA005 point at the *target* symbol's declaration as the fix site. When the target lives in a namespace the consumer can't edit (the BCL, third-party packages), the warning is unfixable noise. The analyzer skips those diagnostics when the target's containing namespace matches one of the configured patterns.

**Default**: `System*,Microsoft*` — covers `System.Console`, `System.IO.File.WriteAllText`, `Microsoft.Extensions.Logging.ILogger`, etc.

**Override** via `.editorconfig`:

```ini
stringsyntax.suppressed_target_namespaces = System*,Microsoft*,MyCompany.Legacy*
```

Patterns support a trailing `*` (prefix match) or an exact match. Empty config means "no namespaces suppressed".


## Name conventions (opt-in)

When enabled, a member or local whose name matches a known convention is treated as if it already carries the corresponding `[StringSyntax]` value. `[Uri] string url`, `string pageHtml`, and `string emailAddress` all become Present-by-name without any attribute or `//language=` comment.

**Enable** via `.editorconfig`:

```ini
stringsyntax.name_conventions = enabled
```

**Convention list** (matchers are case-insensitive; PascalCase suffix also matches, so `pageHtml` matches `Html` but `myhtml` does not — a word boundary is required):

| Value      | Matches        | Example suffixes              |
|------------|----------------|-------------------------------|
| `Uri`      | `uri`, `url`   | `pageUrl`, `apiUri`, `baseUrl`|
| `Html`     | `html`         | `pageHtml`, `bodyHtml`        |
| `Json`     | `json`         | `payloadJson`, `requestJson`  |
| `Xml`      | `xml`          | `configXml`, `responseXml`    |
| `Regex`    | `regex`        | `pathRegex`, `nameRegex`      |
| `Sql`      | `sql`          | `selectSql`, `querySql`       |
| `Csv`      | `csv`          | `rowCsv`, `exportCsv`         |
| `Yaml`     | `yaml`         | `manifestYaml`, `configYaml`  |
| `Markdown` | `markdown`     | `bodyMarkdown`, `notesMarkdown`|
| `Email`    | `email`        | `userEmail`, `contactEmail`   |

Format-style constants (`DateTimeFormat`, `NumericFormat`, ...) and the generic `Text` are deliberately omitted — their natural variable names (`format`, `text`) are too broad to safely promote.

**Effects** when enabled:

- A name match promotes the symbol to Present at every analysis site — SSA001/SSA002/SSA003/SSA004/SSA005 reason about it as if it carried the matching attribute.
- The convention overrides `KnownUnannotatedAssemblies` suppression: a third-party API's `string url` parameter is treated as `Uri` even though the assembly carries no `[StringSyntax]` annotations. (Names that don't match a convention still fall back to the default suppression.)
- **SSA008** fires when an existing `[StringSyntax]`, shortcut (`[Html]`), single-value `[ReturnSyntax]`, or `//language=` comment carries the same value the name already implies. The codefix removes the redundant annotation.
- Methods and return values are excluded — a method named `GetUrl` does not propagate the convention through its return value, and `[ReturnSyntax]` annotations are never flagged as redundant by name.
- `[UnionSyntax(...)]` is excluded — multi-value annotations cannot be replaced by a single-name convention.


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
<sup><a href='/src/Tests/Samples.cs#L11-L26' title='Snippet source file'>snippet source</a> | <a href='#snippet-FormatMismatch' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/Samples.cs#L28-L42' title='Snippet source file'>snippet source</a> | <a href='#snippet-MissingSourceFormat' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/Samples.cs#L44-L59' title='Snippet source file'>snippet source</a> | <a href='#snippet-DroppedFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SSA003 is the weakest signal — discarding format metadata is usually benign. Consider disabling it in projects where it produces noise.


### Matching formats — no diagnostic

<!-- snippet: MatchingFormat -->
<a id='snippet-MatchingFormat'></a>
```cs
public void MatchingCall(RegexHolder holder) =>
    ConsumeRegexStrict(holder.Pattern); // no diagnostic
```
<sup><a href='/src/Tests/Samples.cs#L61-L66' title='Snippet source file'>snippet source</a> | <a href='#snippet-MatchingFormat' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/Tests/Samples.cs#L68-L73' title='Snippet source file'>snippet source</a> | <a href='#snippet-LiteralSource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

#### Other non-resolvable expressions

Concatenations, interpolations, and `await` expressions don't reduce to a single symbol, so they're Unknown too.

<!-- snippet: OtherUnknownSource -->
<a id='snippet-OtherUnknownSource'></a>
```cs
public void ConcatCall(string suffix) =>
    ConsumeRegexStrict("[a-z]" + suffix); // no diagnostic — concatenation is Unknown
```
<sup><a href='/src/Tests/Samples.cs#L75-L80' title='Snippet source file'>snippet source</a> | <a href='#snippet-OtherUnknownSource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Icon

[Pattern](https://thenounproject.com/icon/pattern-8046712/) from [The Noun Project](https://thenounproject.com).

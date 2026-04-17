# StringSyntaxAttributeAnalyzer

[![Build status](https://img.shields.io/appveyor/build/SimonCropp/StringSyntaxAttributeAnalyzer)](https://ci.appveyor.com/project/SimonCropp/StringSyntaxAttributeAnalyzer)
[![NuGet Status](https://img.shields.io/nuget/v/StringSyntaxAttributeAnalyzer.svg?label=StringSyntaxAttributeAnalyzer)](https://www.nuget.org/packages/StringSyntaxAttributeAnalyzer/)

Roslyn analyzer that reports mismatches between [`StringSyntaxAttribute`](https://learn.microsoft.com/en-us/dotnet/api/system.diagnostics.codeanalysis.stringsyntaxattribute) values when a string flows from one annotated member to another.

**See [Milestones](../../milestones?state=closed) for release notes.**


## Diagnostics

| ID     | Severity | Code fix | Description                                                       |
|--------|----------|----------|-------------------------------------------------------------------|
| SSA001 | Warning  | —        | Format mismatch — both sides have `StringSyntax` but values differ |
| SSA002 | Warning  | Yes      | Source has no `StringSyntax` while the target requires one         |
| SSA003 | Warning  | Yes      | Source has `StringSyntax` while the target has none                |

SSA002 and SSA003 ship a code fix that adds `[StringSyntax("<value>")]` to the relevant declaration — the source symbol for SSA002, the target symbol for SSA003. SSA001 has no fix because picking which side to change requires human judgement.


## Analyzed Sites

Diagnostics fire on:

 * Method, constructor, indexer, and delegate arguments
 * Simple assignments (`=`), including inside object initializers
 * Property and field inline initializers


## Sources the analyzer can resolve

The source of a string is resolved when it is one of:

 * A property reference (`obj.Prop`)
 * A field reference (`obj._field`)
 * A parameter reference (method argument, lambda parameter, etc.)

Other sources — string literals, interpolated strings, local variables, method invocations, concatenation, `await`, binary expressions, etc. — are treated as **unknown** and suppress all three diagnostics. This avoids noise on every `"foo"`, every `ToString()`, and every local variable passed to a `[StringSyntax]` parameter. `StringSyntaxAttribute` itself cannot be applied to return values, so method invocations cannot carry a known syntax.


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
<sup><a href='/src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs#L12-L27' title='Snippet source file'>snippet source</a> | <a href='#snippet-FormatMismatch' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs#L29-L43' title='Snippet source file'>snippet source</a> | <a href='#snippet-MissingSourceFormat' title='Start of snippet'>anchor</a></sup>
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
<sup><a href='/src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs#L45-L60' title='Snippet source file'>snippet source</a> | <a href='#snippet-DroppedFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->

SSA003 is the weakest signal — discarding format metadata is usually benign. Consider disabling it in projects where it produces noise.


### Matching formats — no diagnostic

<!-- snippet: MatchingFormat -->
<a id='snippet-MatchingFormat'></a>
```cs
public void MatchingCall(RegexHolder holder) =>
    ConsumeRegexStrict(holder.Pattern); // no diagnostic
```
<sup><a href='/src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs#L62-L67' title='Snippet source file'>snippet source</a> | <a href='#snippet-MatchingFormat' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


### Literal source — no diagnostic

String literals, local variables, method invocations, and other expressions without a resolvable symbol are treated as unknown and do not trigger SSA002.

<!-- snippet: LiteralSource -->
<a id='snippet-LiteralSource'></a>
```cs
public void LiteralCall() =>
    ConsumeRegexStrict("[a-z]+"); // no diagnostic — literal is Unknown
```
<sup><a href='/src/StringSyntaxAttributeAnalyzer.Tests/Samples.cs#L69-L74' title='Snippet source file'>snippet source</a> | <a href='#snippet-LiteralSource' title='Start of snippet'>anchor</a></sup>
<!-- endSnippet -->


## Icon

[Pattern](https://thenounproject.com/icon/pattern-8046712/) from [The Noun Project](https://thenounproject.com).

// ReSharper disable ReturnValueOfPureMethodIsNotUsed
// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable MemberCanBePrivate.Local
#pragma warning disable CS0414
#pragma warning disable CA1822

public class Samples
{
    #region FormatMismatch

    public class MismatchHolder
    {
        [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
        public string Format { get; set; } = "";
    }

    public void ConsumeRegex([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
    {
    }

    public void FormatMismatchCall(MismatchHolder holder) =>
        // SSA001
        ConsumeRegex(holder.Format);

    #endregion

    #region MissingSourceFormat

    public class UntypedHolder
    {
        public string Value { get; set; } = "";
    }

    public void ConsumeRegexStrict([StringSyntax(StringSyntaxAttribute.Regex)] string pattern)
    {
    }

    public void MissingSourceCall(UntypedHolder holder) =>
        // SSA002
        ConsumeRegexStrict(holder.Value);

    #endregion

    #region DroppedFormat

    public class RegexHolder
    {
        [StringSyntax(StringSyntaxAttribute.Regex)]
        public string Pattern { get; set; } = "";
    }

    public void ConsumeAnyString(string value)
    {
    }

    public void DroppedFormatCall(RegexHolder holder) =>
        // SSA003
        ConsumeAnyString(holder.Pattern);

    #endregion

    #region MatchingFormat

    public void MatchingCall(RegexHolder holder) =>
        // no diagnostic
        ConsumeRegexStrict(holder.Pattern);

    #endregion

    #region LiteralSource

    public void LiteralCall() =>
        // no diagnostic — literal is Unknown
        ConsumeRegexStrict("[a-z]+");

    #endregion

    #region OtherUnknownSource

    public void ConcatCall(string suffix) =>
        // no diagnostic — concatenation is Unknown
        ConsumeRegexStrict("[a-z]" + suffix);

    #endregion

    #region RecordPrimaryCtorParameter

    public record PatternRecord([StringSyntax(StringSyntaxAttribute.Regex)] string Pattern);

    public void RecordCall(PatternRecord record) =>
        // no diagnostic — attribute flows to property
        ConsumeRegexStrict(record.Pattern);

    #endregion
}

namespace TaggedCollectionSamples
{
    using System.Collections.Generic;
    using System.Diagnostics.CodeAnalysis;
    using System.Linq;

#region TaggedCollectionLinqLambda

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

#endregion

#region TaggedCollectionForEach

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

#endregion

#region TaggedCollectionUserExtension

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

#endregion

#region DictionaryPositional

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

#endregion

#region DictionaryKeyPositional

public class SyntaxIndex
{
    // Dictionary<string, V> with a non-string V: the tag applies to Key.
    // Reads via kv.Key / .Keys.First() flow the tag; kv.Value is untagged.
    [StringSyntax("Html")]
    public Dictionary<string, int> Lengths { get; set; } = [];
}

#endregion

#region AnonymousProjectionLanguageComment

// Payload is a plain untagged string — there's nothing on the source for
// the analyzer to inherit, so a naive projection would flow as Unknown.
// (If Payload were `[StringSyntax("Json")]`, the tag would already flow
// through the anon read automatically — no comment required.)
public class DataRow
{
    public string Payload { get; set; } = "";
}

public class AnonProjectionReader
{
    public IEnumerable<DataRow> Rows { get; set; } = [];

    public void ConsumeJson([StringSyntax("Json")] string value) { }

    public void ConsumeRegex([StringSyntax("Regex")] string value) { }

    public void Go()
    {
        // Annotate the anon member initializer with `//language=<name>` to
        // give the projected value a tag that flows both ways through the
        // anon instance (write-site validation and read-side propagation).
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

#endregion

#region AsyncLinqElementReturn

// Stand-in for EF Core's EntityFrameworkQueryableExtensions. Any element-
// returning LINQ name + "Async" returning Task<T> / ValueTask<T> participates
// by shape — no hard-coded list of method names.
public static class AsyncQueryable
{
    public static Task<T> SingleAsync<T>(this IQueryable<T> source) =>
        Task.FromResult(source.Single());
}

public class AsyncProjectionReader
{
    public IQueryable<DataRow> Rows { get; set; } = null!;

    public void ConsumeJson([StringSyntax("Json")] string value) { }

    public async Task Go()
    {
        // The tag on DataRow.Payload flows through .Select(_ => _.Payload) and
        // survives the await + SingleAsync — `payload` carries Json.
        var payload = await Rows.Select(_ => _.Payload).SingleAsync();
        // no diagnostic
        ConsumeJson(payload);
    }
}

#endregion

}

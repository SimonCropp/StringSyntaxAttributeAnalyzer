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
        ConsumeRegex(holder.Format); // SSA001

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
        ConsumeRegexStrict(holder.Value); // SSA002

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
        ConsumeAnyString(holder.Pattern); // SSA003

    #endregion

    #region MatchingFormat

    public void MatchingCall(RegexHolder holder) =>
        ConsumeRegexStrict(holder.Pattern); // no diagnostic

    #endregion

    #region LiteralSource

    public void LiteralCall() =>
        ConsumeRegexStrict("[a-z]+"); // no diagnostic — literal is Unknown

    #endregion

    #region OtherUnknownSource

    public void ConcatCall(string suffix) =>
        ConsumeRegexStrict("[a-z]" + suffix); // no diagnostic — concatenation is Unknown

    #endregion

    #region RecordPrimaryCtorParameter

    public record PatternRecord([StringSyntax(StringSyntaxAttribute.Regex)] string Pattern);

    public void RecordCall(PatternRecord record) =>
        ConsumeRegexStrict(record.Pattern); // no diagnostic — attribute flows to property

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
        bodies.Values.Select(s => { Consume(s); return s; }).ToList();
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

}

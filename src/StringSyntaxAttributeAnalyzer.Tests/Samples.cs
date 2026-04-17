// ReSharper disable UnusedMember.Local
// ReSharper disable UnusedParameter.Local
// ReSharper disable ClassNeverInstantiated.Local
// ReSharper disable MemberCanBePrivate.Local
#pragma warning disable CS0414
#pragma warning disable CA1822

using System.Diagnostics.CodeAnalysis;

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
}

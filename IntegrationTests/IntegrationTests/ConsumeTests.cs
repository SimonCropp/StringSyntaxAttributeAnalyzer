using System.Diagnostics.CodeAnalysis;

public class ConsumeTests
{
    [Test]
    public void MatchingStringSyntaxBuildsClean()
    {
        var sample = new Sample();
        sample.AssignMatching();
    }

    [Test]
    public void KnownMismatchWithSuppressionBuildsClean()
    {
        var sample = new Sample();
        sample.AssignSuppressedMismatch();
    }
}

public class Sample
{
    [StringSyntax(StringSyntaxAttribute.Regex)]
    public string Pattern { get; set; } = "";

    [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
    public string Format { get; set; } = "";

    public static void ConsumeRegex([StringSyntax(StringSyntaxAttribute.Regex)] string value)
    {
    }

    public void AssignMatching() =>
        ConsumeRegex(Pattern);

#pragma warning disable SSA001
    public void AssignSuppressedMismatch() =>
        ConsumeRegex(Format);
#pragma warning restore SSA001
}

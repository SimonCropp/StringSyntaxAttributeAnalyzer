[TestFixture]
public class ConsumeTests
{
    [Test]
    public void SyntaxStyle_MatchingStringSyntaxBuildsClean()
    {
        var sample = new SyntaxStyleSample();
        sample.AssignMatching();
    }

    [Test]
    public void SyntaxStyle_KnownMismatchWithSuppressionBuildsClean()
    {
        var sample = new SyntaxStyleSample();
        sample.AssignSuppressedMismatch();
    }

    [Test]
    public void BclStyle_MatchingStringSyntaxBuildsClean()
    {
        var sample = new BclStyleSample();
        sample.AssignMatching();
    }

    [Test]
    public void BclStyle_KnownMismatchWithSuppressionBuildsClean()
    {
        var sample = new BclStyleSample();
        sample.AssignSuppressedMismatch();
    }

    [Test]
    public void GeneratedSyntaxConstants_AreAvailable()
    {
        // Compile-time: this line fails to build if the source generator did not emit
        // the Syntax class into the consumer compilation.
        AreEqual("Html", Syntax.Html);
        AreEqual("Regex", Syntax.Regex);
        AreEqual("Json", Syntax.Json);
    }
}

// Uses the generated constants from the bundled source generator.
public class SyntaxStyleSample
{
    [StringSyntax(Syntax.Regex)]
    public string Pattern { get; set; } = "";

    [StringSyntax(Syntax.DateTimeFormat)]
    public string Format { get; set; } = "";

    public static void ConsumeRegex([StringSyntax(Syntax.Regex)] string value)
    {
    }

    public void AssignMatching() =>
        ConsumeRegex(Pattern);

#pragma warning disable SSA001
    public void AssignSuppressedMismatch() =>
        ConsumeRegex(Format);
#pragma warning restore SSA001
}

// Uses the BCL constants directly — the older, non-generated style.
public class BclStyleSample
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

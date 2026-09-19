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
    public void PolyfilledLibrary_MatchingStringSyntaxBuildsClean()
    {
        var sample = new PolyfilledLibrarySample();
        sample.AssignMatching();
    }

    [Test]
    public async Task GeneratedSyntaxConstants_AreAvailable()
    {
        // Compile-time: this line fails to build if the source generator did not emit
        // the Syntax class into the consumer compilation.
#pragma warning disable TUnitAssertions0005
        await Assert.That(Syntax.Html).IsEqualTo("Html");
        await Assert.That(Syntax.Regex).IsEqualTo("Regex");
        await Assert.That(Syntax.Json).IsEqualTo("Json");
#pragma warning restore TUnitAssertions0005
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

// Calls across an assembly boundary into a library that carries its own
// StringSyntaxAttribute (SourceOnlyAttributeConsumer targets netstandard2.0). The
// attribute on PolyfilledApi.TakeJson is a different symbol from the BCL one this
// assembly uses, so the annotation is only seen when the two are matched by name and
// namespace. Unseen, this call reports SSA003 — an error here, given WarningsAsErrors.
public class PolyfilledLibrarySample
{
    [StringSyntax(Syntax.Json)]
    public string Payload { get; set; } = "{}";

    public void AssignMatching() =>
        PolyfilledApi.TakeJson(Payload);
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

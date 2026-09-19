public class RemoveRedundantConventionCodeFixProviderTests
{
    [Test]
    public async Task SSA008_RemovesAttribute_WhenSoleAttributeOnProperty()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "StringSyntax");
        await Contains(fixedSource, "public string Url { get; set; }");
    }

    [Test]
    public async Task SSA008_RemovesAttribute_OnParameter()
    {
        var source =
            """
            public class Holder
            {
                public void Use([StringSyntax("Html")] string pageHtml) { }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "StringSyntax");
        await Contains(fixedSource, "string pageHtml");
    }

    [Test]
    public async Task SSA008_RemovesLanguageComment_PrecedingLine()
    {
        var source =
            """
            public class Holder
            {
                public void Use()
                {
                    // language=html
                    string pageHtml = "<p/>";
                    System.Console.WriteLine(pageHtml);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "language=");
        await Contains(fixedSource, "string pageHtml");
    }

    [Test]
    public async Task SSA008_LeavesUnrelatedCommentMentioningLanguage_Untouched()
    {
        // Regression: IsLanguageTrivia must not strip any comment that merely
        // contains the substring "language" — only the real `// language=X`
        // trivia. With the old loose match the codefix removed the first
        // matching comment (here, the prose one) and left the injection
        // directive in place.
        var source =
            """
            public class Holder
            {
                public void Use()
                {
                    // notes about our query language preferences
                    // language=html
                    string pageHtml = "<p/>";
                    System.Console.WriteLine(pageHtml);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await DoesNotContain(fixedSource, "language=html");
        await Contains(fixedSource, "notes about our query language preferences");
        await Contains(fixedSource, "string pageHtml");
    }

    [Test]
    public async Task SSA008_KeepsDocCommentWhenRemovingAttribute()
    {
        // RemoveNode with KeepNoTrivia drops the attribute list's leading trivia, which
        // is where the member's doc comment lives.
        var source =
            """
            public class Holder
            {
                /// <summary>The endpoint.</summary>
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; } = "";
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "/// <summary>The endpoint.</summary>");
    }

    [Test]
    public async Task SSA008_KeepsRegionDirectiveWhenRemovingAttribute()
    {
        // Same trivia loss, but the discarded trivia is a directive: removing the
        // `#region` leaves an unmatched `#endregion`, which is CS1028 on the next parse.
        var source =
            """
            public class Holder
            {
                #region Endpoints
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; } = "";
                #endregion
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "#region Endpoints");
    }

    [Test]
    public async Task SSA008_KeepsIndentationOfMultiLineParameter()
    {
        var source =
            """
            public class Holder
            {
                public void Use(
                    [StringSyntax("Html")] string pageHtml,
                    int count)
                {
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "        string pageHtml,");
    }

    static async Task Contains(string actual, string expected) =>
        await Assert.That(actual).Contains(expected);

    static async Task DoesNotContain(string actual, string unexpected) =>
        await Assert.That(actual).DoesNotContain(unexpected);

    static Task<string> ApplyFix(string source) =>
        CodeFixVerify.Apply<RemoveRedundantConventionCodeFixProvider>(
            source,
            new()
            {
                DiagnosticId = "SSA008",
                ConfigOptions = new(StringComparer.OrdinalIgnoreCase)
                {
                    ["stringsyntax.name_conventions"] = "enabled",
                }
            });
}

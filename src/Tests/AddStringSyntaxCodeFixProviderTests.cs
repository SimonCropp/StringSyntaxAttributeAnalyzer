public class AddStringSyntaxCodeFixProviderTests
{
    [Test]
    public async Task SSA002_AddsAttributeToSourceProperty()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        await Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceField()
    {
        var source =
            """
            public class Holder
            {
                public string Field;

                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(Field);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        await Contains(fixedSource, "public string Field;");
    }

    [Test]
    public async Task SSA002_AddsAttributeToSourceParameter()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use(string input) => Consume(input);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)] string input");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetParameter()
    {
        var source =
            """
            public class Target
            {
                public static void Consume(string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => Target.Consume(Pattern);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)] string value");
    }

    [Test]
    public async Task SSA003_AddsAttributeToTargetProperty()
    {
        var source =
            """
            public class Target
            {
                public string Value { get; set; }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Value = Pattern;
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        await Contains(fixedSource, "public string Value { get; set; }");
    }

    [Test]
    public async Task SSA005_AddsAttributeToUnattributedEqualitySide()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public string Raw { get; set; }

                public bool Check() => Pattern == Raw;
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        await Contains(fixedSource, "public string Raw { get; set; }");
    }

    [Test]
    public async Task SSA006_ReplacesSingletonUnionWithStringSyntax()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html")]
                public string Markup { get; set; }
            }
            """;

        var fixedSource = await ApplyFix<ReplaceUnionWithStringSyntaxCodeFixProvider>(source);

        // Lowercase `"html"` resolves to the canonical `Html` constant — same logic
        // as the rest of the codefix, kept consistent so the singleton-union rewrite
        // doesn't degrade to a string literal when a known constant exists.
        await Contains(fixedSource, "[Syntax(Syntax.Html)]");
        await Assert.That(fixedSource.Contains("[UnionSyntax")).IsFalse();
    }

    [Test]
    public async Task Fix_WithoutSyntaxAlias_FallsBackToStringSyntax()
    {
        // Consumer opted out of the generator's global usings — no `SyntaxAttribute` alias
        // in scope. The fixer should emit the long form `[StringSyntax(...)]` so the
        // result compiles.
        var source =
            """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFixWithoutAlias(source);

        await Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        await Assert.That(fixedSource.Contains("[Syntax(")).IsFalse();
    }

    // The `StringSyntaxAnalyzer_EmitGlobalUsings=false` shape: the generator still
    // emits the attribute definitions and the `Syntax` constants, but no global usings,
    // so neither the `SyntaxAttribute` alias nor the StringSyntaxAttributeAnalyzer
    // namespace is in scope unless the source imports it.
    static Task<string> ApplyFixWithoutAlias(string source) =>
        CodeFixVerify.Apply(source, new() { EmitGlobalUsings = false });

    [Test]
    public async Task SSA002_CustomFormatValue()
    {
        var source =
            """
            public class Holder
            {
                public string Value { get; set; }

                public static void Consume([StringSyntax("custom-format")] string value) { }

                public void Use() => Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(\"custom-format\")]");
    }

    [Test]
    public async Task SSA002_UnknownValue_TitleAndEmissionUseLiteral()
    {
        // Values outside the generator's Syntax class (checked via
        // KnownSyntaxConstants) have no named constant to reference, so both the
        // title and the emitted attribute fall back to a string literal.
        var source =
            """
            public class Holder
            {
                public string Value { get; set; }

                public static void Consume([StringSyntax("custom-format")] string value) { }

                public void Use() => Consume(Value);
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Add [Syntax(\"custom-format\")] to property 'Value'");
    }

    [Test]
    public async Task MultiDeclaratorField_NoFixRegistered()
    {
        var source =
            """
            public class Holder
            {
                public string a, b;

                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(a);
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SSA002_ExactOutputShape_WithExistingUsings()
    {
        var source =
            """
            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Console.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        await Assert.That(fixedSource.StartsWith('\n')).IsFalse();
        await Assert.That(fixedSource.StartsWith('\r')).IsFalse();
    }

    [Test]
    public async Task SSA002_WithGlobalUsing_DoesNotAddLocalUsing()
    {
        // Consumer has `global using System.Diagnostics.CodeAnalysis;` in another file, so
        // the fix-target file never had its own local using. A naive codefix would add a
        // redundant local using which later tooling strips, leaving blank trivia.
        var targetSource =
            """
            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;
        var globalUsings = "global using System.Diagnostics.CodeAnalysis;\n";

        var fixedSource = await ApplyFixWithGlobalUsings(targetSource, globalUsings);

        Console.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        await Assert.That(fixedSource.StartsWith('\n')).IsFalse();
        await Assert.That(fixedSource.Contains("using System.Diagnostics.CodeAnalysis;")).IsFalse();
    }

    static Task<string> ApplyFixWithGlobalUsings(string source, string globalUsings) =>
        CodeFixVerify.Apply(source, new() { SupportSource = globalUsings });

    [Test]
    public async Task SSA002_ExactOutputShape_NoExistingUsings()
    {
        var source =
            """
            public class Target
            {
                public static void Consume([System.Diagnostics.CodeAnalysis.StringSyntax("Regex")] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        Console.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        await Assert.That(fixedSource.StartsWith('\n')).IsFalse();
        await Assert.That(fixedSource.StartsWith('\r')).IsFalse();
    }

    [Test]
    public async Task SSA002_AddsReturnSyntaxToSourceMethod()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string GetPattern() => "[a-z]+";

                public void Use(Target target) => target.Consume(GetPattern());
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[ReturnSyntax(Syntax.Regex)]");
        await Contains(fixedSource, "public string GetPattern()");
    }

    [Test]
    public async Task SSA002_MethodFix_TitleUsesReturnSyntax()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string GetPattern() => "[a-z]+";

                public void Use(Target target) => target.Consume(GetPattern());
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Add [ReturnSyntax(Syntax.Regex)] to method 'GetPattern'");
    }

    [Test]
    public async Task SSA002_AddsLanguageCommentToLocal()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        // Rider/IntelliJ convention: `regexp` for regex tokens.
        await Contains(fixedSource, "// language=regexp");
        await Contains(fixedSource, "var pattern = \"[a-z]+\";");
    }

    [Test]
    public async Task SSA002_LocalFix_PreservesBlankLineAboveLocal()
    {
        // A blank-line separator above the local must stay *above* the inserted
        // comment — previously its EOL trivia was pushed below the comment,
        // leaving a redundant blank line between `// language=X` and the `var`.
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    System.Console.WriteLine("prelude");

                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);
        var normalized = fixedSource.Replace("\r\n", "\n");

        await Assert.That(normalized).Contains("// language=regexp\n        var pattern");
        await Assert.That(normalized).DoesNotContain("// language=regexp\n\n");
    }

    [Test]
    public async Task SSA002_LocalFix_TitleUsesLanguageComment()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Add //language=regexp to local 'pattern'");
    }

    [Test]
    public async Task SSA002_UnionTarget_LocalSource_OffersUnionAndPerValueLanguageComments()
    {
        var source =
            """
            public class Target
            {
                [UnionSyntax("Json", "Csv")]
                public string FileContents { get; set; }
            }

            public class Holder
            {
                public void Use(Target target)
                {
                    var fileContents = "{}";
                    target.FileContents = fileContents;
                }
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(3);
        await Assert.That(actions[0].Title).IsEqualTo("Add //language=json|csv to local 'fileContents'");
        await Assert.That(actions[1].Title).IsEqualTo("Add //language=json to local 'fileContents'");
        await Assert.That(actions[2].Title).IsEqualTo("Add //language=csv to local 'fileContents'");
    }

    [Test]
    public async Task SSA002_UnionTarget_LocalSource_ApplyUnionLanguageCommentFix()
    {
        var source =
            """
            public class Target
            {
                [UnionSyntax("Json", "Csv")]
                public string FileContents { get; set; }
            }

            public class Holder
            {
                public void Use(Target target)
                {
                    var fileContents = "{}";
                    target.FileContents = fileContents;
                }
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 0);

        await Contains(fixedSource, "// language=json|csv");
    }

    [Test]
    public async Task SSA002_LocalFix_LowercasesJsonToken()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }

                public void Use()
                {
                    var payload = "{}";
                    Consume(payload);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "// language=json");
    }

    // Compound PascalCase constants like `DateOnlyFormat` must not be fully
    // lowercased — the analyzer's value compare is first-char-case-insensitive
    // but ordinal beyond that, so `dateonlyformat` would no longer match
    // `DateOnlyFormat` on round-trip. Only the first character is lowered.
    [Test]
    public async Task SSA002_LocalFix_CompoundTokenPreservesInnerCase()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string value) { }

                public void Use()
                {
                    var outputFormat = "dd/MMM/yyyy";
                    Consume(outputFormat);
                }
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "// language=dateOnlyFormat");
        await Assert.That(fixedSource.Contains("dateonlyformat")).IsFalse();
    }

    [Test]
    public async Task SSA002_LocalFix_CompoundTokenTitlePreservesInnerCase()
    {
        var source =
            """
            public class Holder
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.DateOnlyFormat)] string value) { }

                public void Use()
                {
                    var outputFormat = "dd/MMM/yyyy";
                    Consume(outputFormat);
                }
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Add //language=dateOnlyFormat to local 'outputFormat'");
    }

    [Test]
    public async Task SSA003_UnionSource_OffersUnionAndPerValueFixes()
    {
        var source =
            """
            public class Target
            {
                public string Body { get; set; }
            }

            public class Holder
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }

                public Target Create() => new Target { Body = Body };
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(3);
        await Assert.That(actions[0].Title).IsEqualTo("Add [UnionSyntax(Syntax.Html, Syntax.Xml)] to property 'Body'");
        await Assert.That(actions[1].Title).IsEqualTo("Add [Syntax(Syntax.Html)] to property 'Body'");
        await Assert.That(actions[2].Title).IsEqualTo("Add [Syntax(Syntax.Xml)] to property 'Body'");
    }

    [Test]
    public async Task SSA003_UnionSource_ApplyUnionFix()
    {
        var source =
            """
            public class Target
            {
                public string Body { get; set; }
            }

            public class Holder
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }

                public Target Create() => new Target { Body = Body };
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 0);

        await Contains(fixedSource, "[UnionSyntax(Syntax.Html, Syntax.Xml)]");
    }

    [Test]
    public async Task SSA003_UnionSource_ApplySingleValueFix()
    {
        var source =
            """
            public class Target
            {
                public string Body { get; set; }
            }

            public class Holder
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }

                public Target Create() => new Target { Body = Body };
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 2);

        await Contains(fixedSource, "[Syntax(Syntax.Xml)]");
    }

    [Test]
    public async Task SSA002_UnionTarget_MethodSource_OffersReturnSyntaxUnionAndPerValueFixes()
    {
        var source =
            """
            public class Target
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }
            }

            public class Holder
            {
                public string Build() => "<x/>";

                public void Use(Target target) => target.Body = Build();
            }
            """;

        var actions = await GetCodeActions(source);

        await Assert.That(actions.Length).IsEqualTo(3);
        await Assert.That(actions[0].Title).IsEqualTo("Add [ReturnSyntax(Syntax.Html, Syntax.Xml)] to method 'Build'");
        await Assert.That(actions[1].Title).IsEqualTo("Add [ReturnSyntax(Syntax.Html)] to method 'Build'");
        await Assert.That(actions[2].Title).IsEqualTo("Add [ReturnSyntax(Syntax.Xml)] to method 'Build'");
    }

    [Test]
    public async Task SSA002_UnionTarget_MethodSource_ApplyUnionFix()
    {
        var source =
            """
            public class Target
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }
            }

            public class Holder
            {
                public string Build() => "<x/>";

                public void Use(Target target) => target.Body = Build();
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 0);

        await Contains(fixedSource, "[ReturnSyntax(Syntax.Html, Syntax.Xml)]");
        await Contains(fixedSource, "public string Build()");
    }

    [Test]
    public async Task SSA002_UnionTarget_MethodSource_ApplySingleValueFix()
    {
        var source =
            """
            public class Target
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }
            }

            public class Holder
            {
                public string Build() => "<x/>";

                public void Use(Target target) => target.Body = Build();
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 1);

        await Contains(fixedSource, "[ReturnSyntax(Syntax.Html)]");
    }

    static async Task Contains(string actual, string expected) =>
        await Assert.That(actual).Contains(expected);

    static Task<string> ApplyFixAtIndex(string source, int index) =>
        CodeFixVerify.Apply(source, new() { ActionIndex = index });

    [Test]
    public async Task SSA002_WithShortcutsOptedIn_UsesParameterlessShortcut()
    {
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class RegexAttribute : System.Attribute;
            }

            public class Target
            {
                public void Consume([StringSyntaxAttributeAnalyzer.Regex] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Regex]");
        await Assert.That(fixedSource.Contains("[Syntax(Syntax.Regex)]")).IsFalse();
    }

    [Test]
    public async Task SSA003_WithShortcutsOptedIn_UsesParameterlessShortcutOnParameter()
    {
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class RegexAttribute : System.Attribute;
            }

            public class Target
            {
                public static void Consume(string value) { }
            }

            public class Holder
            {
                [StringSyntaxAttributeAnalyzer.Regex]
                public string Pattern { get; set; }

                public void Use() => Target.Consume(Pattern);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Regex] string value");
    }

    [Test]
    public async Task SSA007_ReplacesStringSyntaxWithShortcut()
    {
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public string Body { get; set; } = "";
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Html]");
        await Assert.That(fixedSource.Contains("[StringSyntax(\"Html\")]")).IsFalse();
    }

    [Test]
    public async Task SSA007_ReturnSyntaxOnMethod_InMultiAttributeList_UsesReturnTarget()
    {
        // Regression: when [ReturnSyntax(X)] sits in a multi-attribute list on a
        // method (here alongside [System.Obsolete]), the codefix must split the
        // shortcut into its own [return: Html] list. Inlining [Html] at method
        // target produces [Html, System.Obsolete] which is a compile error —
        // HtmlAttribute's AttributeUsage doesn't include Method.
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property | System.AttributeTargets.ReturnValue, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Holder
            {
                [ReturnSyntax("Html"), System.Obsolete]
                public string Build() => "<p/>";
            }
            """;

        var fixedSource = await ApplyFix(source, "SSA007");

        await Contains(fixedSource, "[return: Html]");
        await Contains(fixedSource, "System.Obsolete");
        await Assert.That(fixedSource.Contains("[Html,")).IsFalse();
        await Assert.That(fixedSource.Contains("Html, System.Obsolete")).IsFalse();
        await Assert.That(fixedSource.Contains("ReturnSyntax(")).IsFalse();
    }

    [Test]
    public async Task SSA007_ReturnSyntaxOnMethod_ReplacesWithReturnTargetShortcut()
    {
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property | System.AttributeTargets.ReturnValue, AllowMultiple = false)]
                sealed class JsonAttribute : System.Attribute;
            }

            public class Holder
            {
                [ReturnSyntax("Json")]
                public string Build() => "{}";
            }
            """;

        var fixedSource = await ApplyFix(source, "SSA007");

        await Contains(fixedSource, "[return: Json]");
        await Assert.That(fixedSource.Contains("[ReturnSyntax(")).IsFalse();
    }

    [Test]
    public async Task SSA007_ReturnSyntaxOnMethod_TitleUsesReturnTarget()
    {
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property | System.AttributeTargets.ReturnValue, AllowMultiple = false)]
                sealed class JsonAttribute : System.Attribute;
            }

            public class Holder
            {
                [ReturnSyntax("Json")]
                public string Build() => "{}";
            }
            """;

        var actions = await GetCodeActions(source, "SSA007");

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Replace with [return: Json]");
    }

    [Test]
    public async Task SSA007_StringSyntaxOnProperty_TitleUsesBareShortcut()
    {
        // Sanity-check that the non-method path still reports the original title.
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property | System.AttributeTargets.ReturnValue, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public string Body { get; set; } = "";
            }
            """;

        var actions = await GetCodeActions(source, "SSA007");

        await Assert.That(actions.Length).IsEqualTo(1);
        await Assert.That(actions[0].Title).IsEqualTo("Replace with [Html]");
    }

    [Test]
    public async Task SSA002_LowercaseValue_NormalizesToCanonicalConstant()
    {
        // Target spelled lowercase `"html"` (matches via SyntaxValueMatcher's
        // first-char case-insensitive rule). Without shortcut opt-in, the codefix
        // should still surface `Syntax.Html` rather than degrading to a literal.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("html")] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[Syntax(Syntax.Html)]");
    }

    [Test]
    public async Task SSA002_LowercaseValueWithShortcutsOptedIn_UsesParameterlessShortcut()
    {
        // Reproduces the LegislationApi scenario: the consumer writes
        // `[Syntax("html")]` (lowercase) on the target and has opted into shortcut
        // attributes. The codefix should resolve `"html"` → canonical `Html` and
        // emit `[Html]`, not fall back to `[Syntax("html")]`.
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Target
            {
                public void Consume([StringSyntax("html")] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        // Multiple diagnostics now fire: SSA002 on the source Holder.Value (what this
        // test exercises) and SSA007 on the lowercase `[StringSyntax("html")]` itself
        // (covered separately by the AddShortcut test).
        var fixedSource = await ApplyFix(source, "SSA002");

        await Contains(fixedSource, "[Html]");
        await Assert.That(fixedSource.Contains("[Syntax(\"html\")]")).IsFalse();
        await Assert.That(fixedSource.Contains("[Syntax(Syntax.Html)]")).IsFalse();
    }

    [Test]
    public async Task SSA007_LowercaseValue_ReplacesWithShortcut()
    {
        // Opted-in consumer has written `[StringSyntax("html")]` (lowercase). SSA007
        // fires with the canonical `Html`, codefix rewrites it as `[Html]`.
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Holder
            {
                [StringSyntax("html")]
                public string Body { get; set; } = "";
            }
            """;

        var fixedSource = await ApplyFix(source, "SSA007");

        await Contains(fixedSource, "[Html]");
        await Assert.That(fixedSource.Contains("StringSyntax(\"html\")")).IsFalse();
    }

    [Test]
    public async Task SSA003_LowercaseUnionOption_NormalizesToCanonical()
    {
        // Union option spelled lowercase should still produce the canonical
        // `Syntax.Html` / `Syntax.Xml` references (and a `[Html]` shortcut for the
        // single-value branch when shortcuts are opted in is covered separately).
        var source =
            """
            public class Target
            {
                public string Body { get; set; }
            }

            public class Holder
            {
                [UnionSyntax("html", "xml")]
                public string Body { get; set; }

                public Target Create() => new Target { Body = Body };
            }
            """;

        var fixedSource = await ApplyFixAtIndex(source, 0);

        await Contains(fixedSource, "[UnionSyntax(Syntax.Html, Syntax.Xml)]");
    }

    [Test]
    public async Task SSA006_LowercaseSingletonUnion_NormalizesToCanonical()
    {
        // Singleton-union rewrite (`[UnionSyntax("html")]` → `[Syntax(...)]`) should
        // resolve to the canonical `Syntax.Html` constant rather than the literal
        // string the user wrote.
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html")]
                public string Body { get; set; } = "";
            }
            """;

        var fixedSource = await ApplyFix<ReplaceUnionWithStringSyntaxCodeFixProvider>(source);

        await Contains(fixedSource, "[Syntax(Syntax.Html)]");
    }

    [Test]
    public async Task SSA002_KeepsDocCommentAttachedToProperty()
    {
        // AddAttributeLists prepends the new list to the declaration, but the member's
        // leading trivia stays on the token that used to come first — so the `///` ends
        // up between the attribute and the modifiers, where it is no longer a doc
        // comment (CS1587) and no longer documents the member.
        var source =
            """
            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                /// <summary>The pattern.</summary>
                public string Value { get; set; } = "";

                public void Use() => Target.Consume(Value);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(
            fixedSource,
            """
                /// <summary>The pattern.</summary>
                [Syntax(Syntax.Regex)]
            """);
    }

    [Test]
    public async Task SSA002_DelegateReturn_FixClearsTheWarning()
    {
        // `[ReturnSyntax]` targets Method | Delegate, so on a delegate declaration it
        // lands on the delegate *type*. GetSyntax reads the attributes of `Invoke`,
        // which has none, so the fix applies cleanly and changes nothing.
        var source =
            """
            public delegate string Producer();

            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }
            }

            public class Holder
            {
                public void Use(Producer producer) => Target.Consume(producer());
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(fixedSource, "[ReturnSyntax(Syntax.Json)]");
    }

    [Test]
    public async Task SSA002_LambdaParameterInFieldInitializer_FixTargetsTheParameter()
    {
        // AttributeHost.Find walks to the nearest enclosing VariableDeclaratorSyntax
        // before it looks for a parameter, so a lambda parameter inside a field
        // initializer resolves to the field. The attribute lands on `Compile` and the
        // warning on `pattern` stays.
        var source =
            """
            using System;
            using System.Text.RegularExpressions;

            public class Holder
            {
                static readonly Func<string, Regex> Compile = (string pattern) => new Regex(pattern);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(
            fixedSource,
            "Compile = ([Syntax(Syntax.Regex)] string pattern) => new Regex(pattern);");
    }

    [Test]
    public async Task SSA002_Indexer_OffersAFix()
    {
        // AttributeHost.Find has no IndexerDeclarationSyntax case, so no action is
        // registered and the warning is unactionable. An indexer is a property, so
        // [StringSyntax] is legal on it.
        var source =
            """
            public class Rows
            {
                public string this[int index] => "";
            }

            public class Target
            {
                public static void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }
            }

            public class Holder
            {
                public void Use(Rows rows) => Target.Consume(rows[0]);
            }
            """;

        var fixedSource = await ApplyFix(source);

        await Contains(
            fixedSource,
            """
                [Syntax(Syntax.Json)]
                public string this[int index] => "";
            """);
    }

    [Test]
    public async Task SSA002_WithoutGlobalUsings_MethodFixCompiles()
    {
        // `ReturnSyntax` lives in the StringSyntaxAttributeAnalyzer namespace, which is
        // only imported by the generator's global usings. The single-value property fix
        // falls back to `[StringSyntax("…")]` in this mode; the method fix does not, and
        // writes a name that does not resolve.
        var source =
            """
            using System.Diagnostics.CodeAnalysis;

            public class Target
            {
                public static void Consume([StringSyntax("Json")] string value) { }
            }

            public class Holder
            {
                public static string GetPayload() => "{}";

                public void Use() => Target.Consume(GetPayload());
            }
            """;

        var fixedSource = await CodeFixVerify.Apply(source, new() { EmitGlobalUsings = false });

        await Contains(
            fixedSource,
            """
                [StringSyntaxAttributeAnalyzer.ReturnSyntax("Json")]
                public static string GetPayload() => "{}";
            """);
    }

    [Test]
    public async Task SSA002_PropertySetterValue_FixTargetsTheProperty()
    {
        // The implicit `value` parameter can carry no attribute and has no declaration, so
        // a diagnostic reported against it has no fix site at all. The property is both
        // where the annotation belongs and where it can be written.
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Json)]
                string payload = "";

                public string Payload
                {
                    get => payload;
                    set => payload = value;
                }
            }
            """;

        // The getter returns the same annotated field, so SSA009 fires on this property
        // too — both rules want the annotation in the same place.
        var fixedSource = await ApplyFix(source, "SSA002");

        await Contains(
            fixedSource,
            """
                [Syntax(Syntax.Json)]
                public string Payload
            """);
    }

    static Task<string> ApplyFix(string source, string? diagnosticId = null) =>
        CodeFixVerify.Apply(source, new() { DiagnosticId = diagnosticId });

    static Task<string> ApplyFix<TProvider>(string source, string? diagnosticId = null)
        where TProvider : CodeFixProvider, new() =>
        CodeFixVerify.Apply<TProvider>(source, new() { DiagnosticId = diagnosticId });

    static Task<ImmutableArray<CodeAction>> GetCodeActions(string source, string? diagnosticId = null) =>
        CodeFixVerify.Actions<AddStringSyntaxCodeFixProvider>(
            source,
            new() { DiagnosticId = diagnosticId });
}

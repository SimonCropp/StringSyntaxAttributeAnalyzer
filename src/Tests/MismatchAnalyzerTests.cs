public class MismatchAnalyzerTests
{
    [Test]
    public async Task FormatMismatch_ArgumentToParameter()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        var message = diagnostics[0].GetMessage();
        await Assert.That(message.Contains("DateTimeFormat")).IsTrue();
        await Assert.That(message.Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task FormatMismatch_PropertyToProperty_Assignment()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; }

                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Copy() => Format = Pattern;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task FormatMismatch_ObjectInitializer()
    {
        var source = """
                     public class Target
                     {
                         [StringSyntax(StringSyntaxAttribute.Regex)]
                         public string Pattern { get; set; }
                     }

                     public class Holder
                     {
                         [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                         public string Format { get; set; }

                         public Target Create() => new Target { Pattern = Format };
                     }
                     """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task GenericExtensionMethodReturnSource_InConstructor_IsUnknown()
    {
        var source =
            """
            public static class ConfigExtensions
            {
                public static T GetRequiredValue<T>(this Config c, string key) => default!;
            }
            public class Config { }
            public class AppSettings
            {
                [StringSyntax(StringSyntaxAttribute.Uri)]
                public string Url { get; set; }

                public AppSettings(Config configuration)
                {
                    Url = configuration.GetRequiredValue<string>("URL");
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task MissingSourceFormat_ArgumentToParameter()
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

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
        await Assert.That(diagnostics[0].GetMessage().Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task MissingSourceFormat_PropertyInitializer()
    {
        var source =
            """
            public class Source
            {
                public string Raw { get; set; }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Source src) => Pattern = src.Raw;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task DroppedFormat_AssignPropertyToUnattributed()
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

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
        await Assert.That(diagnostics[0].GetMessage().Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task DroppedFormat_ArgumentToParameter()
    {
        var source =
            """
            public class Target
            {
                public void Consume(string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
    }

    [Test]
    public async Task MatchingFormats_NoDiagnostic()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use(Target target) => target.Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task StringLiteralSource_NoDiagnostic()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Caller
            {
                public void Use(Target target) => target.Consume("[a-z]+");
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task NoStringSyntaxAnywhere_NoDiagnostic()
    {
        var source =
            """
            public class Target
            {
                public void Consume(string value) { }
            }

            public class Holder
            {
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task CustomFormatString_Mismatch()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("custom-a")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("custom-b")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task FirstCharCaseInsensitive_NoDiagnostic()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("json")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("Json")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task MidStringCaseDifference_StillMismatch()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("json")] string value) { }
            }

            public class Holder
            {
                [StringSyntax("jSon")]
                public string Value { get; set; }

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task EqualityMismatch_FiresOnEqualsOperator()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; }

                public bool Check() => Pattern == Format;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA004");
        var message = diagnostics[0].GetMessage();
        await Assert.That(message.Contains("Regex")).IsTrue();
        await Assert.That(message.Contains("DateTimeFormat")).IsTrue();
    }

    [Test]
    public async Task EqualityMismatch_FiresOnNotEqualsOperator()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; }

                public bool Check() => Pattern != Format;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA004");
    }

    [Test]
    public async Task EqualityMatching_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string A { get; set; }

                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string B { get; set; }

                public bool Check() => A == B;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task EqualityWithLiteral_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public bool Check() => Pattern == "[a-z]+";
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task EqualityWithUnattributed_FiresSSA005()
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

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA005");
        await Assert.That(diagnostics[0].GetMessage().Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task EqualityWithUnattributed_RightSide_FiresSSA005()
    {
        var source =
            """
            public class Holder
            {
                public string Raw { get; set; }

                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public bool Check() => Raw == Pattern;
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA005");
    }

    [Test]
    public async Task DroppedFormat_ObjectParameter_NoDiagnostic()
    {
        var source =
            """
            public class Logger
            {
                public static void Log(string message, object value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => Logger.Log("processing {Pattern}", Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DroppedFormat_ParamsObjectArray_NoDiagnostic()
    {
        var source =
            """
            public class Logger
            {
                public static void Log(string message, params object?[] args) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => Logger.Log("processing {Pattern} and {Other}", Pattern, 42);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DroppedFormat_GenericParameter_NoDiagnostic()
    {
        var source =
            """
            public static class Extensions
            {
                public static T Echo<T>(T value) => value;
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public string Use() => Extensions.Echo(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DroppedFormat_StringParameter_StillFires()
    {
        // Regression guard: SSA003 must still fire for genuine string-typed slots —
        // the generic-slot suppression shouldn't swallow real dropped-format cases.
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public static void Consume(string value) { }

                public void Use() => Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
    }

    [Test]
    public async Task DroppedFormat_SystemNamespace_Suppressed()
    {
        // Passing a StringSyntax-attributed string to a System.* API (string.Concat here)
        // would normally fire SSA003 — but we can't add attributes to the BCL, so the
        // default suppression list (System*, Microsoft*) skips it.
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public string Use() => string.Concat(Pattern, "-suffix");
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DroppedFormat_CustomNamespace_NotSuppressed()
    {
        // Regression guard: user code (outside System/Microsoft) should still fire SSA003.
        var source =
            """
            namespace MyLibrary
            {
                public class Target
                {
                    public static void Consume(string value) { }
                }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => MyLibrary.Target.Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
    }

    [Test]
    public async Task DroppedFormat_CustomSuppressionList_Honoured()
    {
        var source =
            """
            namespace MyLegacy
            {
                public class Target
                {
                    public static void Consume(string value) { }
                }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => MyLegacy.Target.Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(
            source,
            editorConfig: "stringsyntax.suppressed_target_namespaces = MyLegacy*");

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task DroppedFormat_PerTreeEditorConfig_Honoured()
    {
        // Real `.editorconfig` `[*.cs]` keys land in GetOptions(tree), not
        // GlobalOptions. This test uses a provider that ONLY exposes the
        // suppression option per-tree, to guard against a regression where the
        // analyzer reads from GlobalOptions only (which would force consumers to
        // use `.globalconfig`).
        var source =
            """
            namespace MyLegacy
            {
                public class Target
                {
                    public static void Consume(string value) { }
                }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; }

                public void Use() => MyLegacy.Target.Consume(Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(
            source,
            editorConfig: "stringsyntax.suppressed_target_namespaces = MyLegacy*",
            perTreeOnly: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task UnionSyntax_OverlappingSets_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html", "xml")]
                public string Markup { get; set; }

                public static void Consume([UnionSyntax("html", "js")] string value) { }

                public void Use() => Consume(Markup);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task UnionSyntax_DisjointSets_FiresSSA001()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html", "xml")]
                public string Markup { get; set; }

                public static void Consume([UnionSyntax("json", "yaml")] string value) { }

                public void Use() => Consume(Markup);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task UnionSyntax_MatchesStringSyntax_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html", "xml")]
                public string Markup { get; set; }

                public static void ConsumeXml([StringSyntax("xml")] string value) { }

                public void Use() => ConsumeXml(Markup);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task StringSyntax_MatchesUnionSyntax_NoDiagnostic()
    {
        // Symmetric to the previous test — source is StringSyntax, target is UnionSyntax.
        var source =
            """
            public class Holder
            {
                [StringSyntax("xml")]
                public string Markup { get; set; }

                public static void ConsumeUnion([UnionSyntax("html", "xml")] string value) { }

                public void Use() => ConsumeUnion(Markup);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task UnionSyntax_SingleOption_FiresSSA006()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html")]
                public string Markup { get; set; }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA006");
        await Assert.That(diagnostics[0].GetMessage().Contains("html")).IsTrue();
    }

    [Test]
    public async Task UnionSyntax_MultipleOptions_NoSSA006()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("html", "xml")]
                public string Markup { get; set; }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task UnionSyntax_EmptyOptions_NoSSA006()
    {
        // Regression: `[UnionSyntax()]` (params array, zero values) previously
        // tripped the singleton path — length < 2 short-circuited to a
        // diagnostic whose message said "has only one option" with an empty
        // "" value, and whose codefix emitted `[StringSyntax("")]`. The zero
        // case isn't a singleton to collapse; it's a user error that should
        // stay silent here (the empty attribute carries no useful info, so
        // the replace-with-StringSyntax fix has nothing to target).
        var source =
            """
            public class Holder
            {
                [UnionSyntax()]
                public string Markup { get; set; }
            }
            """;

        var diagnostics = await GetDiagnostics(source);
        var ssa006 = diagnostics.Where(_ => _.Id == "SSA006").ToArray();
        await Assert.That(ssa006.Length).IsEqualTo(0);
    }

    [Test]
    public async Task UnionSyntax_CrossAssembly_FiresSSA003()
    {
        // Reproduces the real-world case where the UnionSyntax-attributed property lives
        // in a separate assembly (messages package). Each assembly gets its own internal
        // UnionSyntaxAttribute from the generator, so symbol-identity comparison would
        // miss the attribute. Matching by metadata name fixes it.
        var messagesSource =
            """
            public class Message
            {
                [UnionSyntax("html", "xml")]
                public string Body { get; set; }
            }
            """;

        var consumerSource =
            """
            public class Receiver
            {
                public string Body { get; set; }

                public void Copy(Message message) => Body = message.Body;
            }
            """;

        var diagnostics = await GetCrossAssemblyDiagnostics(messagesSource, consumerSource);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
        var message = diagnostics[0].GetMessage();
        await Assert.That(message.Contains("html")).IsTrue();
        await Assert.That(message.Contains("xml")).IsTrue();
    }

    [Test]
    public async Task FieldSource_Mismatch()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Field;

                public void Consume([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string value) { }

                public void Use() => Consume(Field);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task RecordPrimaryCtorParameterAttribute_AppliesToGeneratedProperty()
    {
        var source =
            """
            public record Holder([StringSyntax(StringSyntaxAttribute.Regex)] string Pattern);

            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use(Holder holder) => Consume(holder.Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task RecordPrimaryCtorParameterAttribute_PropertyMismatchAgainstTarget()
    {
        var source =
            """
            public record Holder([StringSyntax(StringSyntaxAttribute.DateTimeFormat)] string Pattern);

            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use(Holder holder) => Consume(holder.Pattern);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ReturnSyntax_MatchingFormat_NoDiagnostic()
    {
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                [ReturnSyntax(StringSyntaxAttribute.Regex)]
                public string GetPattern() => "[a-z]+";

                public void Use() => Consume(GetPattern());
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnSyntax_MismatchedFormat_SSA001()
    {
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                [ReturnSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string GetFormat() => "yyyy";

                public void Use() => Consume(GetFormat());
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ReturnSyntax_MethodWithoutAttribute_FiresSSA002()
    {
        // User-code method returning a string and flowing into a [StringSyntax] target
        // — no [ReturnSyntax], no suppressed namespace — should fire SSA002 and be
        // fixable by adding [ReturnSyntax] to the method.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public string GetPattern() => "[a-z]+";

                public void Use() => Consume(GetPattern());
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task ReturnSyntax_BclMethodSource_Suppressed()
    {
        // string.Format lives under System.* — suppressed_target_namespaces default
        // covers it, so SSA002 should not fire even though the return is not annotated.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use() => Consume(string.Format("{0}", 1));
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnSyntax_UnionValues_MatchingUnionTarget_NoDiagnostic()
    {
        // ReturnSyntax(params string[]) lets a method return-type carry union semantics.
        // Matching [UnionSyntax(...)] on the receiving parameter/property should round-trip
        // cleanly — no mismatch diagnostic.
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Target
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }
            }

            public class Holder
            {
                [ReturnSyntax("Html", "Xml")]
                public string Build() => "<x/>";

                public void Use(Target target) => target.Body = Build();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ReturnSyntax_UnionValues_MismatchedUnionTarget_SSA001()
    {
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Target
            {
                [UnionSyntax("Html", "Xml")]
                public string Body { get; set; }
            }

            public class Holder
            {
                [ReturnSyntax("Json", "Regex")]
                public string Build() => "{}";

                public void Use(Target target) => target.Body = Build();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ReturnSyntax_SourceAnnotated_TargetBare_SSA003()
    {
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Consumer
            {
                public void Consume(string value) { }

                [ReturnSyntax(StringSyntaxAttribute.Regex)]
                public string GetPattern() => "[a-z]+";

                public void Use() => Consume(GetPattern());
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA003");
    }

    [Test]
    public async Task LocalVariable_LanguageComment_MatchingSyntax_NoDiagnostic()
    {
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    // language=regex
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_RegexpToken_NormalizedToRegex()
    {
        // Rider doc uses `regexp`; BCL constant is `Regex`. Verify the normalization
        // so `//language=regexp` matches `[StringSyntax("Regex")]`.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    //language=regexp
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_BlockFormInline_NoDiagnostic()
    {
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    var pattern = /*language=regex*/ "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_Mismatched_FiresSSA001()
    {
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    //language=json
                    var payload = "{}";
                    Consume(payload);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LocalVariable_NoComment_FiresSSA002()
    {
        // An unannotated local flowing into a [StringSyntax] target fires SSA002,
        // fixable by adding a //language=<name> comment above the declaration.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA002");
    }

    [Test]
    public async Task LocalVariable_OutVar_NoDiagnostic()
    {
        // `out var` declares via SingleVariableDesignationSyntax, not
        // LocalDeclarationStatementSyntax — there's no place to attach a
        // `// language=<name>` comment, so the diagnostic would be unfixable.
        // Treat as Unknown (same as an invocation result) rather than firing
        // SSA002 with a codefix that silently lands on the wrong node.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }

                public bool TryGet(string key, out string value) { value = ""; return true; }

                public void Use()
                {
                    if (TryGet("k", out var payload))
                    {
                        Consume(payload);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_PatternDesignation_NoDiagnostic()
    {
        // `is string s` also declares via SingleVariableDesignationSyntax. Same
        // reasoning as out-var: no fixable host, so suppress rather than warn.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }

                public void Use(object obj)
                {
                    if (obj is string s)
                    {
                        Consume(s);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_PrefixOption_Ignored()
    {
        // Rider allows `//language=css prefix=body{ postfix=}`. We ignore the
        // prefix/postfix options — they don't affect syntax identity.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                public void Use()
                {
                    //language=regex prefix=^ postfix=$
                    var pattern = "[a-z]+";
                    Consume(pattern);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_PipeDelimitedUnion_MatchesUnionTarget()
    {
        // `//language=json|csv` expresses the same union shape as
        // `[UnionSyntax(Syntax.Json, Syntax.Csv)]`. The local flows into the
        // union-typed property without SSA002.
        var source =
            """
            public class DataUpload
            {
                [UnionSyntax("Json", "Csv")]
                public string FileContents { get; set; }
            }

            public class Consumer
            {
                public void Use(DataUpload upload)
                {
                    //language=json|csv
                    var fileContents = "{}";
                    upload.FileContents = fileContents;
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_PipeDelimitedUnion_OverlapsSingleTarget()
    {
        // Union source overlaps a single-valued target on `Json` — no mismatch.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Json)] string value) { }

                public void Use()
                {
                    //language=json|csv
                    var payload = "{}";
                    Consume(payload);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LocalVariable_LanguageComment_PipeDelimitedUnion_NoOverlap_FiresSSA001()
    {
        // No overlap between `json|csv` and `Xml` — SSA001.
        var source =
            """
            public class Consumer
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Xml)] string value) { }

                public void Use()
                {
                    //language=json|csv
                    var payload = "{}";
                    Consume(payload);
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_StringSyntaxWithKnownValue_ReportsSSA007()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("Html")]
                public string Body { get; set; } = "";
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA007");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_StringSyntaxWithLowercaseValue_ReportsSSA007()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("html")]
                public string Body { get; set; } = "";
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA007");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
        await Assert.That(diagnostics[0].Properties["StringSyntaxValue"]).IsEqualTo("Html");
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_ReturnShortcutOnMethod_MatchingTarget_NoDiagnostic()
    {
        // `[return: Json]` on a method should round-trip against `[Json]` on the
        // consuming parameter.
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Consumer
            {
                public void Consume([Json] string value) { }

                [return: Json]
                public string Build() => "{}";

                public void Use() => Consume(Build());
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_ReturnShortcutOnMethod_MismatchedTarget_SSA001()
    {
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Consumer
            {
                public void Consume([Regex] string value) { }

                [return: Json]
                public string Build() => "{}";

                public void Use() => Consume(Build());
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_ReturnSyntaxKnownValue_ReportsSSA007()
    {
        // [ReturnSyntax("Json")] with the Json shortcut available — SSA007 should fire
        // suggesting the `[return: Json]` replacement.
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Holder
            {
                [ReturnSyntax("Json")]
                public string Build() => "{}";
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA007");
        await Assert.That(diagnostics[0].Properties["StringSyntaxValue"]).IsEqualTo("Json");
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_ReturnSyntaxUnionValue_NoSSA007()
    {
        // Multi-value ReturnSyntax can't collapse to a single shortcut — SSA007 stays silent.
        var source =
            """
            using StringSyntaxAttributeAnalyzer;

            public class Holder
            {
                [ReturnSyntax("Json", "Html")]
                public string Build() => "{}";
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Count(_ => _.Id == "SSA007")).IsEqualTo(0);
    }

    [Test]
    public async Task ShortcutAttribute_NotOptedIn_StringSyntaxWithKnownValue_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("Html")]
                public string Body { get; set; } = "";
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_StringSyntaxWithUnknownValue_NoDiagnostic()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("my-custom-format")]
                public string Value { get; set; } = "";
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ShortcutAttribute_MatchingTarget_NoDiagnostic()
    {
        var source =
            """
            public class Target
            {
                public void Consume([Regex] string value) { }
            }

            public class Holder
            {
                [Regex]
                public string Value { get; set; } = "";

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ShortcutAttribute_MismatchTarget_ReportsSSA001()
    {
        var source =
            """
            public class Target
            {
                public void Consume([Regex] string value) { }
            }

            public class Holder
            {
                [Html]
                public string Value { get; set; } = "";

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        var message = diagnostics[0].GetMessage();
        await Assert.That(message.Contains("Html")).IsTrue();
        await Assert.That(message.Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_MatchesNameConvention_ReportsSSA008_WithoutConventionsOptIn()
    {
        // `[Html]` applied to a parameter named `html` is self-evidently redundant:
        // the shortcut attribute itself is an opt-in (EmitShortcutAttributes=true),
        // so SSA008 should fire even without the broader `name_conventions` opt-in.
        var source =
            """
            public class Holder
            {
                public void Use([Html] string html) { }
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA008");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
    }

    [Test]
    public async Task ShortcutAttribute_OptedIn_NameDoesNotMatchConvention_NoSSA008()
    {
        var source =
            """
            public class Holder
            {
                public void Use([Html] string body) { }
            }
            """;

        var diagnostics = await GetDiagnostics(source, emitShortcutAttributes: true);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task StringSyntax_NameMatchesConvention_WithoutConventionsOptIn_NoSSA008()
    {
        // Plain `[StringSyntax("Html")]` without the name_conventions opt-in should
        // NOT fire SSA008 — only the shortcut-attribute form bypasses the opt-in.
        var source =
            """
            public class Holder
            {
                public void Use([StringSyntax("Html")] string html) { }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SourceNameExactlyMatchesTargetValue_SuppressesSSA002()
    {
        // A field whose name equals the target's StringSyntax value is
        // self-documenting — no attribute needed, no SSA002.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax("ModifiedBy")] string value) { }
            }

            public class Holder
            {
                public string ModifiedBy = "";

                public void Use(Target target) => target.Consume(ModifiedBy);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task TargetNameExactlyMatchesSourceValue_SuppressesSSA003()
    {
        // Target parameter named `ModifiedBy` receiving a `[StringSyntax("ModifiedBy")]`
        // source is already self-documenting on the target side — no SSA003.
        var source =
            """
            public class Target
            {
                public void Consume(string ModifiedBy) { }
            }

            public class Holder
            {
                [StringSyntax("ModifiedBy")]
                public string Value { get; set; } = "";

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SSA007_StringSyntaxAtReturnTarget_FiresViaReturnAttributesLoop()
    {
        // `[return: StringSyntax("Html")]` is illegal C# (StringSyntaxAttribute's
        // AttributeUsage excludes ReturnValue) but Roslyn still surfaces the
        // attribute through IMethodSymbol.GetReturnTypeAttributes(). The return-
        // attributes loop in AnalyzeSymbolForRedundantStringSyntax exists so
        // SSA007 can fire here — rewriting to `[return: Html]` fixes both the
        // compile error and the redundancy in one go. Regression: keep the loop
        // live; removing it would hide a valid diagnostic on malformed source.
        var source =
            """
            namespace StringSyntaxAttributeAnalyzer
            {
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property | System.AttributeTargets.ReturnValue, AllowMultiple = false)]
                sealed class HtmlAttribute : System.Attribute;
            }

            public class Holder
            {
                [return: StringSyntax("Html")]
                public string Build() => "<p/>";
            }
            """;

        var diagnostics = await GetDiagnostics(source);
        var ssa007 = diagnostics.Where(_ => _.Id == "SSA007").ToArray();
        await Assert.That(ssa007.Length).IsEqualTo(1);
    }

    [Test]
    public async Task LinqLambda_ParameterInheritsElementSyntax_Mismatch()
    {
        // `Values` carries `[StringSyntax("Html")]` on an IEnumerable<string>. The
        // `s` lambda param in Select has no attribute but must inherit "Html" so
        // the mismatched argument to ConsumeRegex fires SSA001.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => Values.Select(s => { ConsumeRegex(s); return s; }).ToList();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
        await Assert.That(diagnostics[0].GetMessage().Contains("Regex")).IsTrue();
    }

    [Test]
    public async Task LinqLambda_ParameterMatchesElementSyntax_NoDiagnostic()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void ConsumeHtml([StringSyntax("Html")] string value) { }

                public void Go() => Values.Select(s => { ConsumeHtml(s); return s; }).ToList();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task SelfReferentialEnumerable_DoesNotStackOverflow()
    {
        // `Node : IEnumerable<Node>` would have infinite-recursed through
        // TryGetKvpTypeArgs → TryGetEnumerableElementType → TryGetKvpTypeArgs
        // before the one-level peel fix.
        var source = """
            using System.Collections;
            using System.Collections.Generic;

            public class Node : IEnumerable<Node>
            {
                public IEnumerator<Node> GetEnumerator() => null!;
                IEnumerator IEnumerable.GetEnumerator() => null!;
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public Node Values { get; set; } = null!;

                public void Go()
                {
                    foreach (var n in Values) { }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);
        await Assert.That(diagnostics.Any(_ => _.Id.StartsWith("AD0001"))).IsFalse();
    }

    [Test]
    public async Task LinqLambda_ExpressionTreePredicate_ComparesAgainstTaggedParameter()
    {
        // Attributes aren't legal on lambdas inside expression trees (CS8972), so
        // lambda-param inference is the only way this pattern can be checked.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Doc
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Bodies { get; set; } = null!;
            }

            public class Service
            {
                public bool Contains(IQueryable<Doc> docs, [StringSyntax("Html")] string needle) =>
                    docs.Any(d => d.Bodies.Any(b => b == needle));
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task LinqLambda_ExpressionTreePredicate_MismatchedSyntaxFires()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Doc
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Bodies { get; set; } = null!;
            }

            public class Service
            {
                public bool Contains(IQueryable<Doc> docs, [StringSyntax("Regex")] string needle) =>
                    docs.Any(d => d.Bodies.Any(b => b == needle));
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA004");
    }

    [Test]
    public async Task LinqFirst_ReturnsElementSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqChain_WhereThenFirst_PreservesElementSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.Where(x => x.Length > 0).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqSelect_IdentityLambda_PreservesElementSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.Select(x => x).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqSelect_MethodGroupWithReturnSyntax_UsesMethodSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                [ReturnSyntax("Html")]
                private static string ToHtml(string v) => v;

                public void Copy() => Target = Values.Select(ToHtml).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
    }

    [Test]
    public async Task LinqSelect_DropsElementSyntax_WhenSelectorUntagged()
    {
        // `.Select(s => s.Length.ToString())` changes element type to an untagged
        // string; element tag should drop at the Select boundary.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public string Target { get; set; } = "";

                public void Copy() => Target = Values.Select(s => s.Length.ToString()).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ForEach_LoopVariableInheritsElementSyntax()
    {
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var s in Values)
                    {
                        ConsumeRegex(s);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task ForEach_NestedOverBodies_FlowsSyntax()
    {
        var source = """
            using System.Collections.Generic;

            public class Doc
            {
                [StringSyntax("Xml")]
                public IEnumerable<string> Bodies { get; set; } = null!;
            }

            public class Service
            {
                public bool Find(IEnumerable<Doc> docs, [StringSyntax("Html")] string needle)
                {
                    foreach (var doc in docs)
                    {
                        foreach (var body in doc.Bodies)
                        {
                            if (body == needle) return true;
                        }
                    }
                    return false;
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA004");
    }

    [Test]
    public async Task UserDefinedExtension_ElementPreserving_PropagatesSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public static class Paged
            {
                public static IEnumerable<T> TakePage<T>(this IEnumerable<T> source, int page, int size) =>
                    source.Skip(page * size).Take(size);
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.TakePage(0, 10).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task UserDefinedExtension_LambdaParamInheritsElementSyntax()
    {
        var source = """
            using System;
            using System.Collections.Generic;

            public static class ForEachExt
            {
                public static void Each<T>(this IEnumerable<T> source, Action<T> callback)
                {
                    foreach (var item in source) callback(item);
                }
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => Values.Each(s => ConsumeRegex(s));
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_KeyStringValue_TagAppliesToKey()
    {
        // Dictionary<string, V> with a non-string value: the [StringSyntax] tag
        // is unambiguously a key-side tag. foreach binds kv to a KVP; kv.Key
        // picks up "Html" and mismatches against the Regex-tagged consumer.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<string, int> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var kv in Values)
                    {
                        ConsumeRegex(kv.Key);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqLambda_ArrayReceiver_InheritsElementSyntax()
    {
        var source = """
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public string[] Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => Values.Select(s => { ConsumeRegex(s); return s; }).ToList();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqSelect_ExpressionBodiedLambda_UsesTaggedInvocation()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Xml")]
                public string Target { get; set; }

                [ReturnSyntax("Html")]
                private static string Echo([StringSyntax("Html")] string v) => v;

                public void Copy() => Target = Values.Select(x => Echo(x)).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        await Assert.That(diagnostics[0].GetMessage().Contains("Html")).IsTrue();
        await Assert.That(diagnostics[0].GetMessage().Contains("Xml")).IsTrue();
    }

    [Test]
    public async Task CollectionTag_DoesNotLeakAsScalar_PassedToUntaggedCollectionParam()
    {
        // A [StringSyntax]-tagged collection passed into a user-owned untagged
        // IEnumerable parameter must not fire SSA003. The tag on a collection-
        // typed member is an element tag, not a scalar tag.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void Accept(IEnumerable<string> list) { }

                public void Go() => Accept(Values);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task ArrayIndexer_ElementAccess_InheritsElementSyntax()
    {
        var source = """
            public class Holder
            {
                [StringSyntax("Html")]
                public string[] Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => ConsumeRegex(Values[0]);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    [Arguments("Single")]
    [Arguments("SingleOrDefault")]
    [Arguments("Last")]
    [Arguments("LastOrDefault")]
    [Arguments("ElementAt")]
    [Arguments("FirstOrDefault")]
    public async Task LinqElementReturning_AllNamedMethods_SurfaceElementSyntax(string methodName)
    {
        var call = methodName == "ElementAt"
            ? $"{methodName}(0)"
            : $"{methodName}()";

        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.{{call}};
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    [Arguments("OrderBy(x => x)")]
    [Arguments("Take(5)")]
    [Arguments("Skip(2)")]
    [Arguments("Distinct()")]
    [Arguments("Reverse()")]
    [Arguments("ToList()")]
    [Arguments("ToArray()")]
    [Arguments("ToHashSet()")]
    [Arguments("AsEnumerable()")]
    [Arguments("Append(\"\")")]
    [Arguments("Prepend(\"\")")]
    public async Task LinqElementPreserving_ChainPropagatesElementSyntax(string call)
    {
        var source = $$"""
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.{{call}}.First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task UserDefinedExtension_StaticFormCall_PropagatesSyntax()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public static class Paged
            {
                public static IEnumerable<T> TakePage<T>(this IEnumerable<T> source, int page, int size) =>
                    source.Skip(page * size).Take(size);
            }

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Paged.TakePage(Values, 0, 10).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task UnionSyntaxOnCollection_ElementInheritsAnyOption()
    {
        // A [UnionSyntax("Html","Xml")] collection carries both values on its
        // elements: AcceptHtml matches via Html; AcceptJson mismatches — fires
        // SSA001.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [UnionSyntax("Html", "Xml")]
                public IEnumerable<string> Values { get; set; } = null!;

                public void AcceptHtml([StringSyntax("Html")] string value) { }
                public void AcceptJson([StringSyntax("Json")] string value) { }

                public void Go()
                {
                    foreach (var s in Values)
                    {
                        AcceptHtml(s);
                        AcceptJson(s);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
        await Assert.That(diagnostics[0].GetMessage().Contains("Json")).IsTrue();
    }

    [Test]
    public async Task ForEach_OverArray_InheritsElementSyntax()
    {
        var source = """
            public class Holder
            {
                [StringSyntax("Html")]
                public string[] Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var s in Values)
                    {
                        ConsumeRegex(s);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task InheritedTag_CollectionOnInterface_FlowsIntoLambda()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public interface IBodies
            {
                [StringSyntax("Html")]
                IEnumerable<string> Bodies { get; }
            }

            public class Impl : IBodies
            {
                public IEnumerable<string> Bodies { get; set; } = null!;
            }

            public class Holder
            {
                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go(Impl impl) =>
                    impl.Bodies.Select(s => { ConsumeRegex(s); return s; }).ToList();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task InheritedTag_CollectionOnAbstractBase_ForeachOverDerived()
    {
        var source = """
            using System.Collections.Generic;

            public abstract class Base
            {
                [StringSyntax("Html")]
                public abstract IEnumerable<string> Bodies { get; }
            }

            public class Derived : Base
            {
                public override IEnumerable<string> Bodies => [];
            }

            public class Holder
            {
                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go(Derived derived)
                {
                    foreach (var s in derived.Bodies)
                    {
                        ConsumeRegex(s);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task InheritedTag_ReturnSyntaxOnInterfaceMethod_FlowsThroughImpl()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public interface ISource
            {
                [ReturnSyntax("Html")]
                IEnumerable<string> Load();
            }

            public class Impl : ISource
            {
                public IEnumerable<string> Load() => [];
            }

            public class Holder
            {
                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy(Impl impl) => Target = impl.Load().First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task InheritedTag_RecordPrimaryCtorParameterOnCollection()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public record Snapshot([StringSyntax("Html")] IEnumerable<string> Values);

            public class Holder
            {
                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy(Snapshot s) => Target = s.Values.First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task LinqSelect_ChangingElementType_DropsSyntax_NoChainLeak()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<string> Values { get; set; } = null!;

                public int Target { get; set; }

                public void Copy() => Target = Values.Select(s => s.Length).First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task Dictionary_KeyValueString_TagAppliesToValue()
    {
        // Dictionary<K, string> with a non-string key: the tag applies to the
        // Value position. Value-read (kv.Value) is tagged; Key-read is not.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var kv in Values)
                    {
                        ConsumeRegex(kv.Value);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_BothStringKeyValue_DefaultsToValue()
    {
        // Dictionary<string, string>: the rule defaults to Value. kv.Value
        // inherits "Html" and mismatches the Regex-tagged consumer. kv.Key
        // stays untagged, so no second diagnostic from a spurious key-read.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<string, string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void ConsumeKey(string k) { }

                public void Go()
                {
                    foreach (var kv in Values)
                    {
                        ConsumeRegex(kv.Value);
                        ConsumeKey(kv.Key);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_IndexerRead_FlowsValueTag()
    {
        // dict[k] resolves to the Value position, so the returned V flows the
        // Html tag into the Regex-tagged consumer.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => ConsumeRegex(Values[0]);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_ValuesFirst_FlowsValueTag()
    {
        // dict.Values is a single-T collection projection of the Value
        // position; its .First() result should carry the Html tag.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, string> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.Values.First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_KeysFirst_FlowsKeyTag()
    {
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<string, int> Values { get; set; } = null!;

                [StringSyntax("Regex")]
                public string Target { get; set; }

                public void Copy() => Target = Values.Keys.First();
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_FirstThenValue_FlowsValueTag()
    {
        // dict.First() returns KVP; the .Value property on that KVP result
        // should surface the Value-position tag of the source dict.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => ConsumeRegex(Values.First().Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_NonStringPositions_Silent()
    {
        // Dictionary<int, int>: neither position is string, so no KVP
        // position can hold a StringSyntax value. The attribute is silently
        // ignored — no diagnostics on reads, and no spurious warnings about
        // the unused attribute.
        var source = """
            using System.Collections.Generic;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, int> Values { get; set; } = null!;

                public int Target { get; set; }

                public void Go() => Target = Values[0];
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(0);
    }

    [Test]
    public async Task IGrouping_StringKey_FlowsThroughDotKey()
    {
        // IGrouping<string, T>: the Key position is string. `grouping.Key`
        // inherits "Html" and mismatches the Regex-tagged consumer.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<IGrouping<string, int>> Groups { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var g in Groups)
                    {
                        ConsumeRegex(g.Key);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task IEnumerableOfKvp_ValueStringFlows()
    {
        // Broad recognition: IEnumerable<KeyValuePair<K,string>> is also a
        // KV stream. A query result shaped like this flows the Value tag.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public IEnumerable<KeyValuePair<int, string>> Entries { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go()
                {
                    foreach (var kv in Entries)
                    {
                        ConsumeRegex(kv.Value);
                    }
                }
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    [Test]
    public async Task Dictionary_WhereChain_PreservesValueTag()
    {
        // Element-preserving LINQ on a KV stream keeps the binding alive —
        // .Where(kv => kv.Value.Length > 0).First().Value should still carry
        // the Value-position tag.
        var source = """
            using System.Collections.Generic;
            using System.Linq;

            public class Holder
            {
                [StringSyntax("Html")]
                public Dictionary<int, string> Values { get; set; } = null!;

                public void ConsumeRegex([StringSyntax("Regex")] string value) { }

                public void Go() => ConsumeRegex(Values.Where(kv => kv.Value.Length > 0).First().Value);
            }
            """;

        var diagnostics = await GetDiagnostics(source);

        await Assert.That(diagnostics.Length).IsEqualTo(1);
        await Assert.That(diagnostics[0].Id).IsEqualTo("SSA001");
    }

    static Task<ImmutableArray<Diagnostic>> GetDiagnostics(
        string source,
        string? editorConfig = null,
        bool perTreeOnly = false,
        bool emitShortcutAttributes = false)
    {
        // Run the package's own source generator so test compilations see what real
        // consumers do: the `global using System.Diagnostics.CodeAnalysis;`, the
        // `Syntax` constants class, and the `UnionSyntaxAttribute` type. Tests don't
        // have to declare any of these themselves.
        var syntaxTree = CSharpSyntaxTree.ParseText(source);

        var baseCompilation = CSharpCompilation.Create(
            "Tests",
            [syntaxTree],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));

        var driver = emitShortcutAttributes
            ? CSharpGeneratorDriver.Create(
                generators: [new SyntaxConstantsGenerator().AsSourceGenerator()],
                additionalTexts: [],
                parseOptions: null,
                optionsProvider: new OptOutOptionsProvider(
                    emitShortcutAttributesValue: "true"))
            : CSharpGeneratorDriver.Create(new SyntaxConstantsGenerator());
        driver.RunGeneratorsAndUpdateCompilation(baseCompilation, out var compilation, out _);

        var analyzer = new MismatchAnalyzer();
        AnalyzerOptions? analyzerOptions = null;
        if (editorConfig is not null)
        {
            var parsed = ParseEditorConfig(editorConfig);
            AnalyzerConfigOptionsProvider provider = perTreeOnly
                ? new PerTreeConfigOptionsProvider(parsed)
                : new TestConfigOptionsProvider(parsed);
            analyzerOptions = new(additionalFiles: [], optionsProvider: provider);
        }

        return compilation
            .WithAnalyzers([analyzer], analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
    }

    static Task<ImmutableArray<Diagnostic>> GetCrossAssemblyDiagnostics(
        string messagesSource,
        string consumerSource)
    {
        var messagesBase = CSharpCompilation.Create(
            "Messages",
            [CSharpSyntaxTree.ParseText(messagesSource)],
            TrustedPlatformReferences.All,
            new(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver
            .Create(new SyntaxConstantsGenerator())
            .RunGeneratorsAndUpdateCompilation(messagesBase, out var messagesCompilation, out _);

        using var messagesStream = new MemoryStream();
        var emit = messagesCompilation.Emit(messagesStream);
        if (!emit.Success)
        {
            var errors = string.Join('\n', emit.Diagnostics.Where(_ => _.Severity == DiagnosticSeverity.Error));
            throw new($"Messages compilation failed:\n{errors}");
        }

        messagesStream.Position = 0;
        var messagesReference = MetadataReference.CreateFromStream(messagesStream);

        var consumerBase = CSharpCompilation.Create(
            "Consumer",
            [CSharpSyntaxTree.ParseText(consumerSource)],
            [..TrustedPlatformReferences.All, messagesReference],
            new(OutputKind.DynamicallyLinkedLibrary));
        CSharpGeneratorDriver
            .Create(new SyntaxConstantsGenerator())
            .RunGeneratorsAndUpdateCompilation(consumerBase, out var consumerCompilation, out _);

        return consumerCompilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
    }

    static Dictionary<string, string> ParseEditorConfig(string content)
    {
        var result = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var line in content.Split('\n'))
        {
            var eq = line.IndexOf('=');
            if (eq < 0)
            {
                continue;
            }

            var key = line[..eq].Trim();
            var value = line[(eq + 1)..].Trim();
            if (key.Length > 0)
            {
                result[key] = value;
            }
        }

        return result;
    }
}

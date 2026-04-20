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

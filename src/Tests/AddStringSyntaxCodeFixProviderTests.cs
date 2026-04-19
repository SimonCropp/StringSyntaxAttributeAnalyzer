[TestFixture]
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        Contains(fixedSource, "public string Value { get; set; }");
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        Contains(fixedSource, "public string Field;");
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)] string input");
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)] string value");
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        Contains(fixedSource, "public string Value { get; set; }");
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

        Contains(fixedSource, "[Syntax(Syntax.Regex)]");
        Contains(fixedSource, "public string Raw { get; set; }");
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
        Contains(fixedSource, "[Syntax(Syntax.Html)]");
        IsFalse(fixedSource.Contains("[UnionSyntax"), $"Expected UnionSyntax removed:\n{fixedSource}");
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

        Contains(fixedSource, "[StringSyntax(\"Regex\")]");
        IsFalse(fixedSource.Contains("[Syntax("),
            $"Expected no [Syntax(...)] attribute when alias isn't in scope:\n{fixedSource}");
    }

    static async Task<string> ApplyFixWithoutAlias(string source)
    {
        // Same shape as PrepareFixAsync but deliberately omits the
        // `global using SyntaxAttribute = ...` alias — simulates the opt-out case.
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TrustedPlatformReferences.All);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var documentId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution.AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.Single();

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);
        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var newDoc = apply.ChangedSolution.GetDocument(document.Id)!;
        return (await newDoc.GetTextAsync()).ToString();
    }

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

        Contains(fixedSource, "[Syntax(\"custom-format\")]");
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

        AreEqual(1, actions.Length);
        AreEqual(
            "Add [Syntax(\"custom-format\")] to property 'Value'",
            actions[0].Title);
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

        AreEqual(0, actions.Length);
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

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed source should not start with a newline");
        IsFalse(fixedSource.StartsWith('\r'), "Fixed source should not start with CR");
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

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed file should not start with a newline");
        IsFalse(
            fixedSource.Contains("using System.Diagnostics.CodeAnalysis;"),
            "Should NOT have added a local using when the namespace is already in a global using");
    }

    static async Task<string> ApplyFixWithGlobalUsings(string source, string globalUsings)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TrustedPlatformReferences.All);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var globalsId = DocumentId.CreateNewId(projectInfo.Id);
        var targetId = DocumentId.CreateNewId(projectInfo.Id);
        solution = solution
            .AddDocument(globalsId, "GlobalUsings.cs", globalUsings)
            .AddDocument(targetId, "Target.cs", source);

        var targetDoc = solution.GetDocument(targetId)!;
        var compilation = (await targetDoc.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();
        var diagnostic = diagnostics.Single();

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            targetDoc,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);
        await new AddStringSyntaxCodeFixProvider()
            .RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var apply = operations.OfType<ApplyChangesOperation>().Single();
        var newDoc = apply.ChangedSolution.GetDocument(targetId)!;
        return (await newDoc.GetTextAsync()).ToString();
    }

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

        TestContext.WriteLine("--- fixed ---\n" + fixedSource + "\n--- end ---");
        IsFalse(fixedSource.StartsWith('\n'), "Fixed source should not start with a newline");
        IsFalse(fixedSource.StartsWith('\r'), "Fixed source should not start with CR");
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

        Contains(fixedSource, "[ReturnSyntax(Syntax.Regex)]");
        Contains(fixedSource, "public string GetPattern()");
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

        AreEqual(1, actions.Length);
        AreEqual("Add [ReturnSyntax(Syntax.Regex)] to method 'GetPattern'", actions[0].Title);
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
        Contains(fixedSource, "// language=regexp");
        Contains(fixedSource, "var pattern = \"[a-z]+\";");
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

        AreEqual(1, actions.Length);
        AreEqual("Add //language=regexp to local 'pattern'", actions[0].Title);
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

        Contains(fixedSource, "// language=json");
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

        AreEqual(3, actions.Length);
        AreEqual(
            "Add [UnionSyntax(Syntax.Html, Syntax.Xml)] to property 'Body'",
            actions[0].Title);
        AreEqual(
            "Add [Syntax(Syntax.Html)] to property 'Body'",
            actions[1].Title);
        AreEqual(
            "Add [Syntax(Syntax.Xml)] to property 'Body'",
            actions[2].Title);
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

        Contains(fixedSource, "[UnionSyntax(Syntax.Html, Syntax.Xml)]");
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

        Contains(fixedSource, "[Syntax(Syntax.Xml)]");
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

        AreEqual(3, actions.Length);
        AreEqual(
            "Add [ReturnSyntax(Syntax.Html, Syntax.Xml)] to method 'Build'",
            actions[0].Title);
        AreEqual(
            "Add [ReturnSyntax(Syntax.Html)] to method 'Build'",
            actions[1].Title);
        AreEqual(
            "Add [ReturnSyntax(Syntax.Xml)] to method 'Build'",
            actions[2].Title);
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

        Contains(fixedSource, "[ReturnSyntax(Syntax.Html, Syntax.Xml)]");
        Contains(fixedSource, "public string Build()");
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

        Contains(fixedSource, "[ReturnSyntax(Syntax.Html)]");
    }

    static void Contains(string actual, string expected) =>
        IsTrue(
            actual.Contains(expected),
            $"Expected fixed source to contain:\n{expected}\n\nActual:\n{actual}");

    static async Task<string> ApplyFixAtIndex(string source, int index)
    {
        var (document, diagnostic) = await PrepareFixAsync(source);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable()[index];
        var operations = await action.GetOperationsAsync(Cancel.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().Single();

        var newDocument = applyOperation.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDocument.GetTextAsync();
        return text.ToString();
    }

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

        Contains(fixedSource, "[Regex]");
        IsFalse(fixedSource.Contains("[Syntax(Syntax.Regex)]"));
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

        Contains(fixedSource, "[Regex] string value");
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

        Contains(fixedSource, "[Html]");
        IsFalse(fixedSource.Contains("[StringSyntax(\"Html\")]"));
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

        Contains(fixedSource, "[return: Json]");
        IsFalse(fixedSource.Contains("[ReturnSyntax("));
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

        AreEqual(1, actions.Length);
        AreEqual("Replace with [return: Json]", actions[0].Title);
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

        AreEqual(1, actions.Length);
        AreEqual("Replace with [Html]", actions[0].Title);
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

        Contains(fixedSource, "[Syntax(Syntax.Html)]");
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

        Contains(fixedSource, "[Html]");
        IsFalse(fixedSource.Contains("[Syntax(\"html\")]"));
        IsFalse(fixedSource.Contains("[Syntax(Syntax.Html)]"));
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

        Contains(fixedSource, "[Html]");
        IsFalse(fixedSource.Contains("StringSyntax(\"html\")"));
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

        Contains(fixedSource, "[UnionSyntax(Syntax.Html, Syntax.Xml)]");
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

        Contains(fixedSource, "[Syntax(Syntax.Html)]");
    }

    static Task<string> ApplyFix(string source, string? diagnosticId = null) =>
        ApplyFix<AddStringSyntaxCodeFixProvider>(source, diagnosticId);

    static async Task<string> ApplyFix<TProvider>(string source, string? diagnosticId = null)
        where TProvider : CodeFixProvider, new()
    {
        var (document, diagnostic) = await PrepareFixAsync(source, diagnosticId);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new TProvider().RegisterCodeFixesAsync(context);

        var action = actions.ToImmutable().Single();
        var operations = await action.GetOperationsAsync(Cancel.None);
        var applyOperation = operations.OfType<ApplyChangesOperation>().Single();

        var newDocument = applyOperation.ChangedSolution.GetDocument(document.Id)!;
        var text = await newDocument.GetTextAsync();
        return text.ToString();
    }

    static async Task<ImmutableArray<CodeAction>> GetCodeActions(string source, string? diagnosticId = null)
    {
        var (document, diagnostic) = await PrepareFixAsync(source, diagnosticId);

        var actions = ImmutableArray.CreateBuilder<CodeAction>();
        var context = new CodeFixContext(
            document,
            diagnostic,
            (action, _) => actions.Add(action),
            Cancel.None);

        await new AddStringSyntaxCodeFixProvider().RegisterCodeFixesAsync(context);
        return actions.ToImmutable();
    }

    static async Task<(Document Document, Diagnostic Diagnostic)> PrepareFixAsync(string source, string? diagnosticId = null)
    {
        var workspace = new AdhocWorkspace();
        var projectInfo = ProjectInfo.Create(
            ProjectId.CreateNewId(),
            VersionStamp.Default,
            name: "Tests",
            assemblyName: "Tests",
            language: LanguageNames.CSharp,
            compilationOptions: new CSharpCompilationOptions(OutputKind.DynamicallyLinkedLibrary),
            metadataReferences: TrustedPlatformReferences.All);

        var solution = workspace.CurrentSolution.AddProject(projectInfo);
        var documentId = DocumentId.CreateNewId(projectInfo.Id);
        var generatedId = DocumentId.CreateNewId(projectInfo.Id);
        // Mirror what SyntaxConstantsGenerator emits: the global using for
        // StringSyntaxAttribute, and the UnionSyntaxAttribute definition. Test sources
        // don't have to declare either. Duplicated here (rather than actually running
        // the generator) because AdhocWorkspace fix pipelines don't pick up generators
        // automatically from a project reference.
        solution = solution
            .AddDocument(generatedId, "Generated.cs", """
                global using System.Diagnostics.CodeAnalysis;
                global using StringSyntaxAttributeAnalyzer;
                global using SyntaxAttribute = System.Diagnostics.CodeAnalysis.StringSyntaxAttribute;

                namespace StringSyntaxAttributeAnalyzer;
                [System.AttributeUsage(System.AttributeTargets.Field | System.AttributeTargets.Parameter | System.AttributeTargets.Property, AllowMultiple = false)]
                sealed class UnionSyntaxAttribute(params string[] options) : System.Attribute;

                [System.AttributeUsage(System.AttributeTargets.Method | System.AttributeTargets.Delegate, AllowMultiple = false)]
                sealed class ReturnSyntaxAttribute(params string[] syntax) : System.Attribute;
                """)
            .AddDocument(documentId, "Test.cs", source);

        var document = solution.GetDocument(documentId)!;
        var compilation = (await document.Project.GetCompilationAsync())!;
        var diagnostics = await compilation
            .WithAnalyzers([new MismatchAnalyzer()])
            .GetAnalyzerDiagnosticsAsync();

        var filtered = diagnosticId is null
            ? diagnostics
            : diagnostics.Where(_ => _.Id == diagnosticId).ToImmutableArray();
        return (document, filtered.Single());
    }

}

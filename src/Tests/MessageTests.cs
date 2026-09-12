// Exact-text assertions for every diagnostic message. The message is the only part of a
// diagnostic that survives into build output, so its wording is a contract: it has to
// name both declarations, spell out the attribute to write, and say where the fix lands.
// A change here is a change to what CI logs and AI agents get to read.
//
// The union cases are the reason this file exists. A multi-value set is rendered as
// `[UnionSyntax("Json", "Xml")]`, never as one value containing the pipe that the
// property bag uses as its machine-readable separator.
public class MessageTests
{
    [Test]
    public async Task SSA001_Argument()
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
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA001");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' is [StringSyntax("DateTimeFormat")] but flows to parameter 'value' of method 'Target.Consume', which is [StringSyntax("Regex")]. Fix: annotate parameter 'value' of method 'Target.Consume' (line 3) as [StringSyntax("DateTimeFormat")], or pass a value that is [StringSyntax("Regex")].""");
    }

    [Test]
    public async Task SSA001_UnionTarget_RendersUnion()
    {
        // The pipe-joined rendering read as a single syntax value literally named
        // "Json|Xml"; a union target has to read as the set of values it accepts.
        var source =
            """
            public class Target
            {
                public void Consume([UnionSyntax("Json", "Xml")] string value) { }
            }

            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA001");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' is [StringSyntax("Regex")] but flows to parameter 'value' of method 'Target.Consume', which is [UnionSyntax("Json", "Xml")]. Fix: annotate parameter 'value' of method 'Target.Consume' (line 3) as [StringSyntax("Regex")], or pass a value that is [UnionSyntax("Json", "Xml")].""");
    }

    [Test]
    public async Task SSA001_UnionSource_RendersUnion()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                [UnionSyntax("Json", "Xml")]
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA001");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' is [UnionSyntax("Json", "Xml")] but flows to parameter 'value' of method 'Target.Consume', which is [StringSyntax("Regex")]. Fix: annotate parameter 'value' of method 'Target.Consume' (line 3) as [UnionSyntax("Json", "Xml")], or pass a value that is [StringSyntax("Regex")].""");
    }

    [Test]
    public async Task SSA002_Argument()
    {
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA002");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' has no StringSyntax attribute but flows to parameter 'value' of method 'Target.Consume', which is [StringSyntax("Regex")]. Fix: add [StringSyntax("Regex")] to property 'Holder.Value' (line 8).""");
    }

    [Test]
    public async Task SSA002_MethodSource_SuggestsReturnSyntax()
    {
        // A method can't host [StringSyntax] — the fixer writes [ReturnSyntax], so the
        // message has to name that, not the attribute the target happens to carry.
        var source =
            """
            public class Target
            {
                public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }
            }

            public class Holder
            {
                public string Build() => "x";

                public void Use(Target target) => target.Consume(Build());
            }
            """;

        var diagnostic = await Single(source, "SSA002");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """method 'Holder.Build' has no StringSyntax attribute but flows to parameter 'value' of method 'Target.Consume', which is [StringSyntax("Regex")]. Fix: add [ReturnSyntax("Regex")] to method 'Holder.Build' (line 8).""");
    }

    [Test]
    public async Task SSA003_Argument()
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
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA003");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' is [StringSyntax("Regex")] but flows to parameter 'value' of method 'Target.Consume', which has no StringSyntax attribute. Fix: add [StringSyntax("Regex")] to parameter 'value' of method 'Target.Consume' (line 3).""");
    }

    [Test]
    public async Task SSA003_UnionSource_RendersUnion()
    {
        var source =
            """
            public class Target
            {
                public void Consume(string value) { }
            }

            public class Holder
            {
                [UnionSyntax("Json", "Xml")]
                public string Value { get; set; } = null!;

                public void Use(Target target) => target.Consume(Value);
            }
            """;

        var diagnostic = await Single(source, "SSA003");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Value' is [UnionSyntax("Json", "Xml")] but flows to parameter 'value' of method 'Target.Consume', which has no StringSyntax attribute. Fix: add [UnionSyntax("Json", "Xml")] to parameter 'value' of method 'Target.Consume' (line 3).""");
    }

    [Test]
    public async Task SSA004_Equality()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; } = null!;

                [StringSyntax(StringSyntaxAttribute.DateTimeFormat)]
                public string Format { get; set; } = null!;

                public bool Same() => Pattern == Format;
            }
            """;

        var diagnostic = await Single(source, "SSA004");

        // Line 6, not 7: a declaration's span starts at its attribute list, and the site
        // named here is deliberately the same node the codefix edits.
        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Pattern' is [StringSyntax("Regex")] but is compared with property 'Holder.Format', which is [StringSyntax("DateTimeFormat")]. Fix: annotate property 'Holder.Format' (line 6) as [StringSyntax("Regex")], or compare a value that is [StringSyntax("DateTimeFormat")].""");
    }

    [Test]
    public async Task SSA005_Equality()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Pattern { get; set; } = null!;

                public string Other { get; set; } = null!;

                public bool Same() => Pattern == Other;
            }
            """;

        var diagnostic = await Single(source, "SSA005");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Holder.Pattern' is [StringSyntax("Regex")] but is compared with property 'Holder.Other', which has no StringSyntax attribute. Fix: add [StringSyntax("Regex")] to property 'Holder.Other' (line 6).""");
    }

    [Test]
    public async Task SSA006_SingletonUnion()
    {
        var source =
            """
            public class Holder
            {
                [UnionSyntax("Json")]
                public string Value { get; set; } = null!;
            }
            """;

        var diagnostic = await Single(source, "SSA006");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """[UnionSyntax("Json")] on property 'Holder.Value' has only one option. Fix: replace it with [StringSyntax("Json")].""");
    }

    [Test]
    public async Task SSA007_RedundantShortcut()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("Html")]
                public string Value { get; set; } = null!;
            }
            """;

        var diagnostic = await Single(source, "SSA007", emitShortcutAttributes: true);

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """The "Html" annotation on property 'Holder.Value' can be written as the shortcut attribute. Fix: replace it with [Html].""");
    }

    [Test]
    public async Task SSA008_RedundantByConvention()
    {
        var source =
            """
            public class Holder
            {
                [StringSyntax("Html")]
                public string PageHtml { get; set; } = null!;
            }
            """;

        var diagnostic = await Single(
            source,
            "SSA008",
            editorConfig: "stringsyntax.name_conventions = true");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """The "Html" annotation on property 'Holder.PageHtml' is redundant: the name already matches the Html convention. Fix: remove the annotation.""");
    }

    [Test]
    public async Task SSA009_MissingReturnAnnotation()
    {
        var source =
            """
            public class Row
            {
                [StringSyntax("Html")]
                public string Reason { get; set; } = null!;

                public string ReasonDisplay => Reason;
            }
            """;

        var diagnostic = await Single(source, "SSA009");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """property 'Row.ReasonDisplay' returns a value tagged "Html" but carries no return annotation. Fix: add [StringSyntax("Html")] to property 'Row.ReasonDisplay' (line 6).""");
    }

    [Test]
    public async Task SSA009_Method_SuggestsReturnSyntax()
    {
        var source =
            """
            public class Row
            {
                [StringSyntax("Html")]
                public string Reason { get; set; } = null!;

                public string Describe() => Reason;
            }
            """;

        var diagnostic = await Single(source, "SSA009");

        await Assert.That(diagnostic.GetMessage()).IsEqualTo(
            """method 'Row.Describe' returns a value tagged "Html" but carries no return annotation. Fix: add [ReturnSyntax("Html")] to method 'Row.Describe' (line 6).""");
    }

    [Test]
    public async Task NestedType_RendersContainingTypes()
    {
        // A raw `ContainingType.Name` concat collapsed `Outer.Inner` to `Inner`, so two
        // members of same-named nested types read identically in a build log.
        var source =
            """
            public class Outer
            {
                public class Inner
                {
                    [StringSyntax(StringSyntaxAttribute.Regex)]
                    public string Value { get; set; } = null!;
                }
            }

            public class Target
            {
                public void Consume(string value) { }

                public void Use(Outer.Inner holder) => Consume(holder.Value);
            }
            """;

        var diagnostic = await Single(source, "SSA003");

        await Assert.That(diagnostic.GetMessage().Contains("property 'Outer.Inner.Value'")).IsTrue();
    }

    [Test]
    public async Task CrossFileDeclaration_RendersPath()
    {
        // The fix site is in another file, so naming only a line number would send the
        // reader to the wrong file. The path has to come along.
        var declaringSource =
            """
            public class Holder
            {
                [StringSyntax(StringSyntaxAttribute.Regex)]
                public string Value { get; set; } = null!;
            }
            """;
        var usingSource =
            """
            public class Target
            {
                public void Consume(string value) { }

                public void Use(Holder holder) => Consume(holder.Value);
            }
            """;

        var diagnostics = await GetDiagnostics(
            [
                CSharpSyntaxTree.ParseText(usingSource, path: "Target.cs"),
                CSharpSyntaxTree.ParseText(declaringSource, path: "Holder.cs")
            ]);
        var diagnostic = diagnostics.Single(_ => _.Id == "SSA003");

        // The fix site here is the *target* parameter, which lives in Target.cs — the
        // same file as the diagnostic — so it renders as a bare line number.
        await Assert.That(diagnostic.GetMessage().Contains("(line 3)")).IsTrue();

        // ...while the SSA002 direction puts the fix site in the other file.
        var reversed = await GetDiagnostics(
            [
                CSharpSyntaxTree.ParseText(
                    """
                    public class Target
                    {
                        public void Consume([StringSyntax(StringSyntaxAttribute.Regex)] string value) { }

                        public void Use(Plain plain) => Consume(plain.Value);
                    }
                    """,
                    path: "Target.cs"),
                CSharpSyntaxTree.ParseText(
                    """
                    public class Plain
                    {
                        public string Value { get; set; } = null!;
                    }
                    """,
                    path: "Plain.cs")
            ]);
        var missing = reversed.Single(_ => _.Id == "SSA002");

        await Assert.That(missing.GetMessage().Contains("(Plain.cs:3)")).IsTrue();
    }

    static async Task<Diagnostic> Single(
        string source,
        string id,
        string? editorConfig = null,
        bool emitShortcutAttributes = false)
    {
        var diagnostics = await GetDiagnostics(
            [CSharpSyntaxTree.ParseText(source)],
            editorConfig,
            emitShortcutAttributes);
        return diagnostics.Single(_ => _.Id == id);
    }

    // Mirrors MismatchAnalyzerTests.GetDiagnostics, but over an explicit tree list so the
    // cross-file cases can give each tree a path.
    static Task<ImmutableArray<Diagnostic>> GetDiagnostics(
        SyntaxTree[] trees,
        string? editorConfig = null,
        bool emitShortcutAttributes = false)
    {
        var baseCompilation = CSharpCompilation.Create(
            "Tests",
            trees,
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

        AnalyzerOptions? analyzerOptions = null;
        if (editorConfig is not null)
        {
            var parsed = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var line in editorConfig.Split('\n'))
            {
                var separator = line.IndexOf('=');
                if (separator < 0)
                {
                    continue;
                }

                parsed[line[..separator].Trim()] = line[(separator + 1)..].Trim();
            }

            analyzerOptions = new(
                additionalFiles: [],
                optionsProvider: new TestConfigOptionsProvider(parsed));
        }

        return compilation
            .WithAnalyzers([new MismatchAnalyzer()], analyzerOptions)
            .GetAnalyzerDiagnosticsAsync();
    }
}

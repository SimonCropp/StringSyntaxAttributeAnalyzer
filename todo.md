# Todo

Bugs found in a code review on 2026-09-17, against commit `88de72e`. Each one was reproduced with a throwaway test. Line numbers refer to that commit, so method names are given as well.


## Crashes and build breaks

- [x] **1. Stack overflow on a self-referential local**
  - Repro: `string s = s;` or `string s = s ?? "";`, followed by any use of `s`, such as `Take(s);`. `var s = s;` does not trigger it.
  - Cause: `GetSourceInfo` ([MismatchAnalyzer.cs:1568](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L1568)) and `TryResolveLocalInitializer` ([MismatchAnalyzer.cs:1866](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L1866)) call each other with no cycle guard.
  - Impact: the code doesn't compile, but IDEs run analyzers on it while typing. A stack overflow can't be caught, so it kills the process. The test host exited with a `Stack overflow.` message.
  - Fix: track the locals already being resolved, or cap the depth.

- [x] **2. The package only loads on compiler 5.9 or newer**
  - [src/Directory.Packages.props:8](src/Directory.Packages.props#L8) pins `Microsoft.CodeAnalysis.CSharp` 5.9.0, the compiler version that ships with SDK 10.0.401.
  - With the .NET 9 SDK (9.0.318, compiler 4.14), the build reports `CS9057` and skips the DLL. The generator is skipped too, so code that uses `[UnionSyntax]` fails with `CS0246`.
  - The .NET 8 SDK (compiler 4.11) and VS 2022 have older compilers, so they are expected to fail the same way.
  - Fix: build against the oldest Roslyn version that has the APIs the analyzer needs, or document the minimum SDK and IDE versions.

- [x] **3. Generated code needs C# 12**
  - `SyntaxConstantsGenerator` emits primary constructors ([SyntaxConstantsGenerator.cs:80](src/StringSyntaxAttributeAnalyzer/SyntaxConstantsGenerator.cs#L80)), file-scoped namespaces, `global using` and `#nullable`.
  - Compiling only the generated files:
    - netstandard2.0 or net4x at their default C# 7.3: 7 errors (`CS8370`).
    - net6 (C# 10) and net7 (C# 11): primary constructors are rejected (`CS8936`, `CS9058`).
  - Those are the targets the `StringSyntaxAttribute` polyfill exists for.
  - The netstandard2.0 integration consumer misses this because `IntegrationTests/Directory.Build.props` sets `LangVersion=preview`.
  - Fix: emit code that compiles at C# 7.3, or adapt the output to the `LanguageVersion` from the parse options. Add an integration consumer that keeps the default LangVersion.

- [x] **4. `[UnionSyntax(null)]` and `[ReturnSyntax(null)]` crash the analyzer**
  - `ExtractUnionOptions` ([SyntaxAttributeExtensions.cs:111](src/StringSyntaxAttributeAnalyzer/SyntaxAttributeExtensions.cs#L111)) reads `first.Values.Length`. For a null array, `Values` is a default `ImmutableArray`, so this throws `NullReferenceException`.
  - The exception surfaces as `AD0001`, and every flow involving that symbol goes unanalyzed.
  - Fix: return an empty array when `first.IsNull`.


## Wrong diagnostics

- [x] **5. Annotations from a library with an internal `StringSyntaxAttribute` polyfill are ignored**
  - Cause: the attribute class is compared by symbol identity (`SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.StringSyntax)`) in `GetSyntaxFromAttributes` ([MismatchAnalyzer.cs:999](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L999)), `TryReportRedundant` ([MismatchAnalyzer.cs:143](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L143)) and `TryGetSingleSyntaxValue` ([MismatchAnalyzer.cs:322](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L322)). An internal polyfill is a different symbol in every assembly, including the one this package's generator emits for netstandard2.0 and net4x.
  - Repro: a netstandard2.0 library exposes `public static void TakeJson([StringSyntax("Json")] string value)`, and another project calls it:
    - passing a Json-tagged value reports `SSA003` "which has no StringSyntax attribute" (expected: no diagnostic)
    - passing an Xml-tagged value reports `SSA003` (expected: `SSA001`)
  - Reproduced with a net10 consumer and with a consumer that has its own polyfill.
  - Fix: match `System.Diagnostics.CodeAnalysis.StringSyntaxAttribute` by name and namespace, the way `IsNamed` already matches `UnionSyntax` and `ReturnSyntax`. Add a cross-assembly test with a polyfilled library.

- [x] **6. Nested lambdas bind the outer parameter to the inner collection**
  - Repro: `JsonDocs.Where(j => XmlDocs.Any(x => Matches(j, x)))`, where `Matches([StringSyntax("Json")] string json, [StringSyntax("Xml")] string xml)`, reports `SSA001`: `parameter 'j' of method 'lambda expression' is [StringSyntax("Xml")]`.
  - Cause: `GetLinqLambdaReceiver` ([LinqExtensions.cs:220](src/StringSyntaxAttributeAnalyzer/LinqExtensions.cs#L220)) takes the lambda nearest to the parameter *reference*, not the lambda that declares the parameter. The anonymous-type tracing path uses the same helper.
  - Fix: walk up to the `IAnonymousFunctionOperation` whose `Symbol` is `param.Parameter.ContainingSymbol`.

- [x] **7. Property setters get a false SSA002 with no fix**
  - Repro:

    ```cs
    [StringSyntax("Json")]
    string payload = "";

    [StringSyntax("Json")]
    public string Payload
    {
        get => payload;
        set => payload = value;
    }
    ```

    This reports `SSA002` on the setter's implicit `value` parameter. The diagnostic has no additional location, so no code fix is offered.
  - Cause: `GetSyntax` ([MismatchAnalyzer.cs:880](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L880)) reads only the attributes of the `value` parameter itself. The property's attribute is never applied to it.
  - Fix: for the implicit `value` of a `set` or `init` accessor, use the attributes of the associated property, and point the fix at the property when it has none.

- [x] **8. `//language=` comments are picked up too broadly**
  - Cause: `LanguageCommentReader.TryRead` ([LanguageCommentReader.cs:24](src/StringSyntaxAttributeAnalyzer/LanguageCommentReader.cs#L24)) and `CanHostLanguageComment` ([Extensions.cs:285](src/StringSyntaxAttributeAnalyzer/Extensions.cs#L285)) accept any *ancestor* `LocalDeclarationStatementSyntax`. `TryRead` then scans every comment inside that statement ([LanguageCommentReader.cs:42](src/StringSyntaxAttributeAnalyzer/LanguageCommentReader.cs#L42)), and `TryParse` accepts `language=` anywhere in a comment.
  - A comment inside a lambda in a local's initializer tags the outer local:

    ```cs
    var result = Compute(() =>
    {
        // language=sql
        var query = "select 1";
        Run(query);
        return "{}";
    });
    TakeJson(result); // SSA001: local 'result' is [StringSyntax("sql")]
    ```

    With name conventions enabled and the outer local named `json`, `SSA008` fires instead, and its fix deletes the inner local's comment, leaving a line of whitespace.
  - Ordinary prose counts too. `// Falls back to language=en when the header is missing` above `var greeting = Greeting();` tags `greeting` as `en`, which then reports `SSA001`.
  - `foreach` and `out var` locals nested inside a local declaration are treated as able to host a comment. They get `SSA002`, while the same code elsewhere stays silent:
    - `var action = new Action(() => { foreach (var item in items) { TakeJson(item); } });` reports `SSA002` on `item`, and the fix adds `// language=json` above `var action`.
    - `var found = map.TryGetValue("k", out var entry);` followed by `TakeJson(entry)` reports `SSA002` on `entry`, and the fix tags `found`. The `if (map.TryGetValue("k", out var entry))` form is silent.
  - Fix: only accept the local's own statement (`VariableDeclaratorSyntax` → `VariableDeclarationSyntax` → `LocalDeclarationStatementSyntax`), skip comments inside nested lambdas and blocks, and require the comment text to start with `language=`.

- [x] **9. Generic members get SSA002, and the fix annotates them for every `T`**
  - Repro, with `class Box<T> { public T Value { get; set; } public T Field; }` and `static Task<T> LoadAsync<T>()`:
    - `TakeJson(box.Value)` and `TakeJson(box.Field)` report `SSA002`, and the fix adds `[Syntax(Syntax.Json)]` to the `T` member.
    - `TakeJson(await Loader.LoadAsync<string>())` reports `SSA002`, and the fix adds `[ReturnSyntax(Syntax.Json)]` to the generic method.
    - `TakeJson(Loader.Load<string>())`, where `Load<T>()` returns `T`, is correctly silent.
  - Cause: `GetSymbol` ([MismatchAnalyzer.cs:856](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L856)) only treats a method as Unknown when its original return type is a bare type parameter.
  - Fix: treat a source as Unknown when the type of its original definition is a type parameter, or a `Task<T>` / `ValueTask<T>` of one. The target side already has a matching rule (`IsGenericValueSlot`).

- [x] **10. A local's `//language=` comment is never checked against its initializer**
  - Repro:

    ```cs
    // language=xml
    var mismatched = JsonProp; // no diagnostic, SSA001 expected

    // language=xml
    var untagged = PlainProp; // no diagnostic, SSA002 expected

    // language=xml
    string assigned;
    assigned = JsonProp; // SSA001, as expected
    ```

  - Cause: no operation action covers local declaration initializers. The operation actions registered at [MismatchAnalyzer.cs:47](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L47) cover only `Argument`, `SimpleAssignment`, `PropertyInitializer`, `FieldInitializer`, `BinaryOperator` and `Loop`.
  - Fix: check the initializer of a local that has a `//language=` comment. Without a comment, the initializer already supplies the local's tag, so there is nothing to check.

- [x] **11. A reassigned local keeps its initializer's tag**
  - Repro: `var current = Json; current = Xml; TakeXml(current);` reports `SSA003` on `current = Xml` (asking for `//language=xml` on `current`), and also `SSA001` claiming `current` is Json.
  - Cause: `TryResolveLocalInitializer` ([MismatchAnalyzer.cs:1866](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L1866)) ignores later assignments.
  - Fix: at minimum avoid the contradictory pair, for example by skipping initializer inference for locals that are assigned again.


## Code fixes that damage code

- [x] **12. Adding an attribute detaches the member's doc comment**
  - Repro: `SSA002` on a property documented with `/// <summary>The pattern.</summary>`. The fix produces:

    ```cs
    [Syntax(Syntax.Regex)]
    /// <summary>The pattern.</summary>
    public string Value { get; set; } = "";
    ```

    This reports `CS1587` when XML docs are generated (an error under `TreatWarningsAsErrors`), and the member loses its docs.
  - Cause: `AttributeNodeBuilder` ([AttributeNodeBuilder.cs:44](src/StringSyntaxAttributeAnalyzer.CodeFixes/AttributeNodeBuilder.cs#L44), also `AddParameterless` and `AddUnionSyntax`) calls `AddAttributeLists` without moving the declaration's leading trivia in front of the new list. The same code path handles fields and methods.
  - Fix: move the leading trivia of the declaration's first token onto the new attribute list.

- [x] **13. The SSA008 fix deletes the trivia around the attribute**
  - Cause: `RemoveAttributeAsync` ([RemoveRedundantConventionCodeFixProvider.cs:99](src/StringSyntaxAttributeAnalyzer.CodeFixes/RemoveRedundantConventionCodeFixProvider.cs#L99) and [:103](src/StringSyntaxAttributeAnalyzer.CodeFixes/RemoveRedundantConventionCodeFixProvider.cs#L103)) removes with `SyntaxRemoveOptions.KeepNoTrivia`, which drops the leading trivia of the attribute list.
  - Effects:
    - The member's `///` doc comment is deleted.
    - A `#region` directly above the attribute is deleted, leaving an orphan `#endregion`, which fails with `CS1028` once the file is parsed again. Other directives such as `#if` are at the same risk.
    - A parameter on its own line loses its indentation: `[StringSyntax("Regex", …)] string regex` becomes `string regex` at column 0.
  - With shortcut attributes enabled, `SSA008` fires on `[Html] public string BodyHtml` even without the `name_conventions` opt-in, so this happens by default in those projects.
  - Fix: keep the leading trivia (move it onto the next token), and add tests for doc comments, `#region` and multi-line parameter lists.

- [x] **14. SSA007 and SSA008 drop `StringSyntax` arguments**
  - Repro: `[StringSyntax("Regex", RegexOptions.IgnorePatternWhitespace)] string pattern`:
    - with shortcut attributes enabled, the `SSA007` fix writes `[Regex]`
    - on a parameter named `regex` with name conventions enabled, the `SSA008` fix removes the attribute
  - Either way the `RegexOptions` are lost.
  - Cause: `TryReportRedundant` ([MismatchAnalyzer.cs:136](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L136)) and `TryGetSingleSyntaxValue` ([MismatchAnalyzer.cs:316](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L316)) only look at the first constructor argument.
  - Fix: don't report when the attribute has arguments beyond the syntax value.

- [x] **15. Fixes land on the enclosing declaration when the fix site is inside an initializer**
  - Repro:
    - `static readonly Func<string, Regex> Compile = (string p) => new Regex(p);` reports `SSA002` on `p`, and the fix adds `[Syntax(Syntax.Regex)]` to the field `Compile`.
    - `var local = (string q) => new Regex(q);` reports `SSA002` on `q`, and the fix adds `// language=regexp` above `local`.
    - The warning remains in both cases. The same lambda in an assignment, `f = (string r) => new Regex(r);`, is fixed correctly.
  - Cause: `AttributeHost.Find` ([AttributeHost.cs:15](src/StringSyntaxAttributeAnalyzer.CodeFixes/AttributeHost.cs#L15)) looks for an enclosing `VariableDeclaratorSyntax` before checking for parameter, method or local-function hosts, so anything nested in an initializer resolves to the field or local. Local functions inside such lambdas (`SSA002`, `SSA009`) are likely affected too.
  - Fix: only take the declarator branch when the node is the declarator itself.

- [x] **16. With `StringSyntaxAnalyzer_EmitGlobalUsings=false`, some fixes produce code that doesn't compile**
  - The union fix writes `[UnionSyntax(Syntax.Json, Syntax.Xml)]` ([AttributeNodeBuilder.cs:58](src/StringSyntaxAttributeAnalyzer.CodeFixes/AttributeNodeBuilder.cs#L58) always uses `Syntax.X`), and the method fix writes `[ReturnSyntax("Json")]` ([AddStringSyntaxCodeFixProvider.cs:172](src/StringSyntaxAttributeAnalyzer.CodeFixes/AddStringSyntaxCodeFixProvider.cs#L172)).
  - No using directive is added, so both fail with `CS0246` / `CS0103` in a file without `using StringSyntaxAttributeAnalyzer;`. The single-value property fix already handles this case by falling back to `[StringSyntax("…")]`.
  - Fix titles always show `Syntax.X` (`BuildFixMetadata` and `FormatArgument`), even when the fix writes a string literal.
  - Fix: in this mode, write string literals and fully qualified (or imported) attribute names, and build the titles from what is actually written.

- [x] **17. The delegate fix never clears the warning**
  - Repro: `public delegate string Producer();` and `TakeJson(producer())` report `SSA002` on `Producer.Invoke`. The fix adds `[ReturnSyntax(Syntax.Json)]` to the delegate declaration, and `SSA002` is still reported afterwards.
  - Cause: `GetSyntax` ([MismatchAnalyzer.cs:937](src/StringSyntaxAttributeAnalyzer/MismatchAnalyzer.cs#L937)) reads attributes from the `Invoke` method, but `[ReturnSyntax]` on a delegate declaration is applied to the delegate type (`ReturnSyntaxAttribute` targets `Delegate`).
  - Fix: for a delegate's `Invoke`, also read the attributes of the containing delegate type.

- [x] **18. User-defined indexers get SSA002 with no fix**
  - Repro: `class Rows { public string this[int i] => ""; }` and `TakeJson(rows[0])` report `SSA002` ("property 'Rows.this'"), and no code action is offered. `SSA003` into an indexer should behave the same.
  - Cause: `AttributeHost.Find` ([AttributeHost.cs:29](src/StringSyntaxAttributeAnalyzer.CodeFixes/AttributeHost.cs#L29)) has no `IndexerDeclarationSyntax` case.
  - Fix: support indexers as an attribute host (including in `AttributeNodeBuilder`), or treat them as Unknown.


## Minor

- [x] **19. The SSA005 docs don't match the code**
  - [docs/SSA005.md:50](docs/SSA005.md#L50) says an unannotated local is Unknown and doesn't warn. It does: `Pattern == input`, with `var input = Console.ReadLine();`, reports `SSA005`.
  - [docs/SSA005.md:52](docs/SSA005.md#L52) says an `object` operand doesn't warn. An `object` *local* does (`(object)Pattern == local`), because `GetTargetType` returns null for locals. An `object` parameter is correctly silent.
  - `SSA005` also lacks the exemption `SSA002` has for unions that include `"Text"`: `Take(Plain)` into `[UnionSyntax("Json", "Text")]` is silent, but `Body == Plain` reports `SSA005`.
  - Fix: update the docs, or change the code to match them.

- [ ] **20. `System*` also suppresses namespaces such as `Systematic.Data`**
  - The pattern is a plain prefix match ([NamespaceSuppression.cs:84](src/StringSyntaxAttributeAnalyzer/NamespaceSuppression.cs#L84)), so the default `System*,Microsoft*` silently exempts any namespace that starts with those letters. `SSA003` into `Systematic.Data.Store.Save` is silent, while the same call into `Acme.Data.Store.Save` fires.
  - The readme documents this as a prefix match, so a change needs a readme update too.
  - Fix: make `X*` match `X` and `X.*` only.

- [x] **21. Diagnostic message wording**
  - Lambdas render as "method 'lambda expression'", constructors as "method 'Regex.Regex'", indexers as "property 'Rows.this'", and generic types as `Box<String>` instead of `Box<string>`. `memberFormat` ([Rules.cs:481](src/StringSyntaxAttributeAnalyzer/Rules.cs#L481)) lacks `SymbolDisplayMiscellaneousOptions.UseSpecialTypes`.
  - `MessageTests` pins the message text, so update the pins along with any change.

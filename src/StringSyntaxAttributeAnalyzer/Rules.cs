// Every diagnostic the analyzer can raise: the descriptors, the property-bag keys the
// code fix reads back, and one Report method per rule.
//
// Analysis code calls `Rules.ReportDropped(...)` rather than assembling a Diagnostic
// inline, so the message arguments, additional-location layout and property bag for a
// given SSA are defined once, next to the descriptor that documents them. The Report
// methods take no view on *whether* to fire — every suppression decision stays in the
// analyzer; these only build and raise.
//
// Roslyn's analysis contexts share no common interface, so each Report method is typed
// to the context its rule is registered against. SSA008 is registered twice and carries
// an overload per context.
static class Rules
{
    // AddStringSyntaxCodeFixProvider duplicates this literal rather than referencing it:
    // the codefix project deliberately has no reference to the analyzer, because a
    // ProjectReference forms a build cycle with the PackAnalyzer target. Keep in sync.
    public const string ValueKey = "StringSyntaxValue";

    // SSA008 tells the fixer which redundancy shape it is looking at — an explicit
    // attribute to delete, or a `//language=` comment to strip.
    public const string ConventionTargetKey = "ConventionTarget";

    // Messages name both declarations and spell out the fix. Build output — and
    // anything reading it, from CI logs to AI agents — only ever sees the message
    // string, so it has to carry enough to act on without opening an IDE: which
    // declaration is wrong, what to write on it, and where that declaration lives.
    // `description` and `helpLinkUri` carry the rest; the help link is what IDEs and
    // SARIF consumers surface as "learn more".
    const string helpRoot = "https://github.com/SimonCropp/StringSyntaxAttributeAnalyzer/blob/main/docs/";

    static readonly DiagnosticDescriptor formatMismatch = new(
        id: "SSA001",
        title: "StringSyntax format mismatch",
        messageFormat: "{0} is {1} but flows to {2}, which is {3}. {4}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "A value annotated with one syntax flows into a target annotated with a different one. Either the target's annotation is wrong, or the wrong value is being passed. Change whichever side is mistaken so both agree. There is no code fix: picking the wrong side would silently launder a real bug.",
        helpLinkUri: helpRoot + "SSA001.md");

    static readonly DiagnosticDescriptor missingSourceFormat = new(
        id: "SSA002",
        title: "Source has no StringSyntax while target requires one",
        messageFormat: "{0} has no StringSyntax attribute but flows to {1}, which is {2}. Fix: add {3} to {0}{4}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An unannotated value flows into an annotated target, so the syntax is unverified on the way in. Add a matching annotation to the source declaration, or rename it to match a known name convention. Apply mechanically with: dotnet format analyzers --diagnostics SSA002.",
        helpLinkUri: helpRoot + "SSA002.md");

    static readonly DiagnosticDescriptor droppedFormat = new(
        id: "SSA003",
        title: "Source has StringSyntax while target has none",
        messageFormat: "{0} is {1} but flows to {2}, which has no StringSyntax attribute. Fix: add {3} to {2}{4}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An annotated value flows into an unannotated target, so the syntax is lost from that point on. Add a matching annotation to the target declaration, or rename it to match a known name convention. Apply mechanically with: dotnet format analyzers --diagnostics SSA003.",
        helpLinkUri: helpRoot + "SSA003.md");

    static readonly DiagnosticDescriptor equalityMismatch = new(
        id: "SSA004",
        title: "Equality comparison between mismatched StringSyntax values",
        messageFormat: "{0} is {1} but is compared with {2}, which is {3}. {4}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Two values annotated with different syntaxes are compared for equality. Strings of different syntaxes are rarely meaningfully equal, so this is usually a mix-up of two similarly-shaped values. There is no code fix, for the same reason as SSA001.",
        helpLinkUri: helpRoot + "SSA004.md");

    static readonly DiagnosticDescriptor equalityMissingFormat = new(
        id: "SSA005",
        title: "Equality comparison with an unattributed value",
        messageFormat: "{0} is {1} but is compared with {2}, which has no StringSyntax attribute. Fix: add {3} to {2}{4}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "An annotated value is compared with an unannotated one. Annotate the bare side so the comparison is checked, or rename it to match a known name convention. Apply mechanically with: dotnet format analyzers --diagnostics SSA005.",
        helpLinkUri: helpRoot + "SSA005.md");

    static readonly DiagnosticDescriptor singletonUnion = new(
        id: "SSA006",
        title: "UnionSyntax with a single option should be StringSyntax",
        messageFormat: "[UnionSyntax(\"{0}\")] on {1} has only one option. Fix: replace it with [StringSyntax(\"{0}\")].",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "[UnionSyntax] expresses a choice between several syntaxes; with a single option it is just [StringSyntax]. Replace it. Apply mechanically with: dotnet format analyzers --diagnostics SSA006.",
        helpLinkUri: helpRoot + "SSA006.md");

    static readonly DiagnosticDescriptor redundantStringSyntax = new(
        id: "SSA007",
        title: "StringSyntax can be replaced with a shortcut attribute",
        messageFormat: "The \"{0}\" annotation on {1} can be written as the shortcut attribute. Fix: replace it with [{0}].",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Shortcut attributes are opted in for this project, so the long form can be written as a bare [Html] / [Json] / [Regex]. Purely cosmetic — the analyzer reads both forms identically. Apply mechanically with: dotnet format analyzers --diagnostics SSA007.",
        helpLinkUri: helpRoot + "SSA007.md");

    static readonly DiagnosticDescriptor redundantByConvention = new(
        id: "SSA008",
        title: "StringSyntax annotation is redundant due to a name convention",
        messageFormat: "The \"{0}\" annotation on {1} is redundant: the name already matches the {0} convention. Fix: remove the annotation.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Name conventions are opted in for this project, so the declaration's name alone already carries this syntax and the annotation restates it. Remove the annotation, or rename the declaration if the name is the part that is wrong. Apply mechanically with: dotnet format analyzers --diagnostics SSA008.",
        helpLinkUri: helpRoot + "SSA008.md");

    static readonly DiagnosticDescriptor missingReturnAnnotation = new(
        id: "SSA009",
        title: "Member returns a tagged value but has no return annotation",
        messageFormat: "{0} returns a value tagged \"{1}\" but carries no return annotation. Fix: add {2} to {0}{3}.",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true,
        description: "Every return in the body resolves to the same annotated syntax, but the member's own signature carries none, so callers see an unannotated string and the syntax stops at the API boundary. Annotate the return. Apply mechanically with: dotnet format analyzers --diagnostics SSA009.",
        helpLinkUri: helpRoot + "SSA009.md");

    public static readonly ImmutableArray<DiagnosticDescriptor> All =
    [
        formatMismatch,
        missingSourceFormat,
        droppedFormat,
        equalityMismatch,
        equalityMissingFormat,
        singletonUnion,
        redundantStringSyntax,
        redundantByConvention,
        missingReturnAnnotation
    ];

    // SSA001. No codefix — picking which side is wrong requires judgement, so there is
    // no fix site to attach and no value to hand the fixer. The fix clause still names
    // both options so a reader of the build log knows what the choice is.
    public static void ReportMismatch(
        OperationAnalysisContext context,
        Location location,
        ISymbol? sourceSymbol,
        SyntaxInfo source,
        ISymbol? targetSymbol,
        SyntaxInfo target) =>
        context.ReportDiagnostic(Diagnostic.Create(
            formatMismatch,
            location,
            DescribeSymbol(sourceSymbol),
            FormatAttribute(source.Values, sourceSymbol),
            DescribeSymbol(targetSymbol),
            FormatAttribute(target.Values, targetSymbol),
            MismatchFix(location, "pass", sourceSymbol, source, targetSymbol, target)));

    // SSA002. `fixTarget` is the untagged source's declaration — the codefix adds a
    // [StringSyntax] there (or [ReturnSyntax] when it is a method) matching `target`.
    // The attribute named in the fix clause is rendered against `fixTarget`, so a
    // method source is told to add [ReturnSyntax], which is what the fixer writes.
    public static void ReportMissingSource(
        OperationAnalysisContext context,
        Location location,
        ISymbol? fixTarget,
        ISymbol? targetSymbol,
        SyntaxInfo target) =>
        context.ReportDiagnostic(CreateFixable(
            missingSourceFormat,
            location,
            fixTarget,
            target,
            [
                DescribeSymbol(fixTarget),
                DescribeSymbol(targetSymbol),
                FormatAttribute(target.Values, targetSymbol),
                FormatAttribute(target.Values, fixTarget),
                Site(location, fixTarget)
            ]));

    // SSA003. Mirror of SSA002: the target is both the fix site and the description
    // rendered into the message, and the tagged side is the source.
    public static void ReportDropped(
        OperationAnalysisContext context,
        Location location,
        ISymbol? sourceSymbol,
        ISymbol? targetSymbol,
        SyntaxInfo source) =>
        context.ReportDiagnostic(CreateFixable(
            droppedFormat,
            location,
            targetSymbol,
            source,
            [
                DescribeSymbol(sourceSymbol),
                FormatAttribute(source.Values, sourceSymbol),
                DescribeSymbol(targetSymbol),
                FormatAttribute(source.Values, targetSymbol),
                Site(location, targetSymbol)
            ]));

    // SSA004. Both sides carry an attribute and the values differ — no codefix, same
    // reasoning as SSA001.
    public static void ReportEqualityMismatch(
        OperationAnalysisContext context,
        Location location,
        ISymbol? leftSymbol,
        SyntaxInfo left,
        ISymbol? rightSymbol,
        SyntaxInfo right) =>
        context.ReportDiagnostic(Diagnostic.Create(
            equalityMismatch,
            location,
            DescribeSymbol(leftSymbol),
            FormatAttribute(left.Values, leftSymbol),
            DescribeSymbol(rightSymbol),
            FormatAttribute(right.Values, rightSymbol),
            MismatchFix(location, "compare", leftSymbol, left, rightSymbol, right)));

    // SSA005. Two descriptions to render ({attributed, bare}) rather than SSA002/003's
    // one, so it builds its own message args instead of going through CreateFixable.
    public static void ReportEqualityMissing(
        OperationAnalysisContext context,
        Location location,
        ISymbol? attributedSymbol,
        SyntaxInfo attributedInfo,
        ISymbol? bareSymbol) =>
        context.ReportDiagnostic(Diagnostic.Create(
            equalityMissingFormat,
            location,
            additionalLocations: GetAdditionalLocations(bareSymbol),
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(ValueKey, SyntaxValueMatcher.FormatValues(attributedInfo.Values)),
            messageArgs:
            [
                DescribeSymbol(attributedSymbol),
                FormatAttribute(attributedInfo.Values, attributedSymbol),
                DescribeSymbol(bareSymbol),
                FormatAttribute(attributedInfo.Values, bareSymbol),
                Site(location, bareSymbol)
            ]));

    // SSA006. The single option travels in the property bag so the fixer can rewrite
    // [UnionSyntax("X")] to [StringSyntax("X")] without re-parsing the attribute.
    // The diagnostic is already on the attribute, so the message names the declaration
    // it sits on rather than repeating a location.
    public static void ReportSingletonUnion(
        SymbolAnalysisContext context,
        Location location,
        string singleValue) =>
        context.ReportDiagnostic(Diagnostic.Create(
            singletonUnion,
            location,
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, singleValue),
            messageArgs: [singleValue, DescribeSymbol(context.Symbol)]));

    // SSA007. `canonical` is the shortcut's declared casing (`Html`, not `html`), which
    // is both the replacement attribute name and what the message renders.
    public static void ReportRedundantShortcut(
        SymbolAnalysisContext context,
        Location location,
        string canonical) =>
        context.ReportDiagnostic(Diagnostic.Create(
            redundantStringSyntax,
            location,
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, canonical),
            messageArgs: [canonical, DescribeSymbol(context.Symbol)]));

    // SSA008. `conventionTarget` selects the fix shape: "Attribute" deletes the
    // annotation, "LanguageComment" strips the trivia. The comment's exact trivia is
    // re-resolved at fix time, so only the declaration location travels here.
    // Reported from two registrations — symbol actions for annotated declarations, a
    // syntax-node action for `//language=` comments on locals — and the two contexts
    // share no interface, hence the pair of overloads over one factory. The syntax-node
    // overload has no `context.Symbol`, so its caller passes the local in.
    public static void ReportRedundantByConvention(
        SymbolAnalysisContext context,
        Location location,
        string conventionValue,
        string conventionTarget) =>
        context.ReportDiagnostic(
            CreateRedundantByConvention(location, conventionValue, conventionTarget, context.Symbol));

    public static void ReportRedundantByConvention(
        SyntaxNodeAnalysisContext context,
        Location location,
        string conventionValue,
        string conventionTarget,
        ISymbol? symbol) =>
        context.ReportDiagnostic(
            CreateRedundantByConvention(location, conventionValue, conventionTarget, symbol));

    static Diagnostic CreateRedundantByConvention(
        Location location,
        string conventionValue,
        string conventionTarget,
        ISymbol? symbol) =>
        Diagnostic.Create(
            redundantByConvention,
            location,
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(ValueKey, conventionValue)
                .Add(ConventionTargetKey, conventionTarget),
            messageArgs: [conventionValue, DescribeSymbol(symbol)]);

    // SSA009. Reported on the member's identifier so the squiggle is tight, with the
    // whole declaration carried alongside as the fix site. The fix site and the
    // diagnostic are the same declaration, so `Site` almost always renders ` (line n)`
    // pointing at the identifier's own line — kept for the rare mapped-path case.
    public static void ReportMissingReturnAnnotation(
        OperationBlockAnalysisContext context,
        Location identifierLocation,
        Location declarationLocation,
        ISymbol symbol,
        string value) =>
        context.ReportDiagnostic(Diagnostic.Create(
            missingReturnAnnotation,
            identifierLocation,
            additionalLocations: [declarationLocation],
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, value),
            messageArgs:
            [
                DescribeSymbol(symbol),
                // The raw value, not an attribute form: what the body returned is a
                // value of that syntax, while the attribute is only what fixes it.
                value,
                FormatAttribute([value], symbol),
                Site(identifierLocation, symbol)
            ]));

    // Shared shape for SSA002/SSA003: the diagnostic carries the values to apply plus
    // the declaration to apply them to. The two rules order their message arguments
    // differently, so callers pass them in already-rendered.
    static Diagnostic CreateFixable(
        DiagnosticDescriptor rule,
        Location location,
        ISymbol? fixTarget,
        SyntaxInfo info,
        string[] messageArgs) =>
        Diagnostic.Create(
            rule,
            location,
            additionalLocations: GetAdditionalLocations(fixTarget),
            // Pipe-delimited so a UnionSyntax source can drive multiple codefix options
            // (one per value + one combined). This is a machine-read separator that the
            // codefix splits back apart — never rendered into a message, where a pipe
            // would read as part of a single syntax value. Messages go through
            // FormatAttribute instead.
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(ValueKey, SyntaxValueMatcher.FormatValues(info.Values)),
            messageArgs: messageArgs);

    // The fix clause for the two rules that have no codefix. Both sides already carry
    // an annotation, so the only honest advice is to name the two ways out: change the
    // other declaration, or supply a value of the syntax it asks for. The "retag" half
    // is dropped when that declaration is not ours to edit (metadata, a library type).
    //
    // `verb` differentiates the flow rule from the equality rule: you *pass* a value to
    // a parameter, you *compare* against an operand.
    static string MismatchFix(
        Location location,
        string verb,
        ISymbol? sourceSymbol,
        SyntaxInfo source,
        ISymbol? targetSymbol,
        SyntaxInfo target)
    {
        if (IsEditable(targetSymbol))
        {
            return $"Fix: annotate {DescribeSymbol(targetSymbol)}{Site(location, targetSymbol)} as {FormatAttribute(source.Values, targetSymbol)}, or {verb} a value that is {FormatAttribute(target.Values, sourceSymbol)}";
        }

        if (IsEditable(sourceSymbol))
        {
            return $"Fix: annotate {DescribeSymbol(sourceSymbol)}{Site(location, sourceSymbol)} as {FormatAttribute(target.Values, sourceSymbol)}, or {verb} a value that is {FormatAttribute(target.Values, sourceSymbol)}";
        }

        return $"Fix: {verb} a value that is {FormatAttribute(target.Values, sourceSymbol)}";
    }

    // Renders a value set the way it is written as an attribute, so a message names the
    // same thing the reader will find on (or add to) the declaration.
    //
    // A multi-value set is a union — `[UnionSyntax("Json", "Xml")]` — not a single value
    // containing a pipe. The pipe is the separator CreateFixable uses in the property
    // bag, which AddStringSyntaxCodeFixProvider splits back apart; it was never meant to
    // reach a message, where `StringSyntax "Json|Xml"` reads as one literal value whose
    // text happens to contain a pipe.
    //
    // Methods carry the annotation on the return, so they read as `[ReturnSyntax(...)]`.
    // The choice of attribute name mirrors what the codefix writes for each host kind,
    // so the message and an applied fix agree.
    static string FormatAttribute(ImmutableArray<string> values, ISymbol? symbol)
    {
        if (values.IsDefaultOrEmpty)
        {
            return "[StringSyntax(\"...\")]";
        }

        var isMethod = symbol is IMethodSymbol;
        if (values.Length == 1)
        {
            return isMethod
                ? $"[ReturnSyntax(\"{values[0]}\")]"
                : $"[StringSyntax(\"{values[0]}\")]";
        }

        var options = string.Join(", ", values.Select(_ => $"\"{_}\""));
        return isMethod
            ? $"[ReturnSyntax({options})]"
            : $"[UnionSyntax({options})]";
    }

    // Where the fix lands, relative to the diagnostic: ` (line 12)` when the declaration
    // shares the diagnostic's file, ` (D:\src\Holder.cs:12)` otherwise, nothing when it
    // has no source location. The fix site is frequently not where the warning is
    // reported — the warning is at the call, the attribute goes on the declaration — so
    // without this a build log names an edit but not the file to make it in.
    static string Site(Location diagnosticLocation, ISymbol? fixTarget)
    {
        var declaration = ResolveDeclarationLocation(fixTarget);
        if (declaration is null ||
            !declaration.IsInSource)
        {
            return "";
        }

        var span = declaration.GetMappedLineSpan();
        var line = span.StartLinePosition.Line + 1;
        if (declaration.SourceTree == diagnosticLocation.SourceTree ||
            string.IsNullOrEmpty(span.Path))
        {
            return $" (line {line})";
        }

        return $" ({span.Path}:{line})";
    }

    static bool IsEditable(ISymbol? symbol) =>
        ResolveDeclarationLocation(symbol) is { IsInSource: true };

    static Location? ResolveDeclarationLocation(ISymbol? symbol)
    {
        var declaration = symbol?.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return Location.Create(declaration.SyntaxTree, declaration.Span);
    }

    static Location[]? GetAdditionalLocations(ISymbol? fixTarget)
    {
        var location = ResolveDeclarationLocation(fixTarget);
        if (location is null)
        {
            return null;
        }

        return [location];
    }

    static string DescribeSymbol(ISymbol? symbol)
    {
        if (symbol is null)
        {
            return "value";
        }

        return symbol switch
        {
            IPropertySymbol property => $"property '{QualifiedName(property)}'",
            IFieldSymbol field => $"field '{QualifiedName(field)}'",
            IParameterSymbol parameter => DescribeParameter(parameter),
            IMethodSymbol method => $"method '{QualifiedName(method)}'",
            ILocalSymbol local => $"local '{local.Name}'",
            _ => "value"
        };
    }

    // Nested types and generics render through a display format rather than a raw
    // `Type.Name` concat, so `Outer.Inner<T>.Value` reads as itself instead of
    // collapsing to `Inner.Value`.
    static readonly SymbolDisplayFormat memberFormat = new(
        typeQualificationStyle: SymbolDisplayTypeQualificationStyle.NameAndContainingTypes,
        genericsOptions: SymbolDisplayGenericsOptions.IncludeTypeParameters,
        memberOptions: SymbolDisplayMemberOptions.IncludeContainingType);

    static string QualifiedName(ISymbol symbol)
    {
        if (symbol.ContainingType is null)
        {
            return symbol.Name;
        }

        return symbol.ToDisplayString(memberFormat);
    }

    static string DescribeParameter(IParameterSymbol parameter)
    {
        if (parameter.ContainingSymbol is IMethodSymbol method)
        {
            return $"parameter '{parameter.Name}' of method '{QualifiedName(method)}'";
        }

        return $"parameter '{parameter.Name}'";
    }
}

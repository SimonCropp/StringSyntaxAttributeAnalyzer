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

    static readonly DiagnosticDescriptor formatMismatch = new(
        id: "SSA001",
        title: "StringSyntax format mismatch",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to {1} with StringSyntax \"{2}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor missingSourceFormat = new(
        id: "SSA002",
        title: "Source has no StringSyntax while target requires one",
        messageFormat: "Value has no StringSyntax attribute but is assigned to {0} with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor droppedFormat = new(
        id: "SSA003",
        title: "Source has StringSyntax while target has none",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to {1} without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMismatch = new(
        id: "SSA004",
        title: "Equality comparison between mismatched StringSyntax values",
        messageFormat: "Comparing {0} (StringSyntax \"{1}\") to {2} (StringSyntax \"{3}\")",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMissingFormat = new(
        id: "SSA005",
        title: "Equality comparison with an unattributed value",
        messageFormat: "Comparing {0} (StringSyntax \"{1}\") to {2} without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor singletonUnion = new(
        id: "SSA006",
        title: "UnionSyntax with a single option should be StringSyntax",
        messageFormat: "[UnionSyntax(\"{0}\")] has only one option; use [StringSyntax(\"{0}\")] instead",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor redundantStringSyntax = new(
        id: "SSA007",
        title: "StringSyntax can be replaced with a shortcut attribute",
        messageFormat: "[StringSyntax(\"{0}\")] can be replaced with [{0}]",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor redundantByConvention = new(
        id: "SSA008",
        title: "StringSyntax annotation is redundant due to a name convention",
        messageFormat: "Annotation \"{0}\" is redundant: the name already matches the {0} convention",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor missingReturnAnnotation = new(
        id: "SSA009",
        title: "Member returns a tagged value but has no return annotation",
        messageFormat: "{0} returns a value tagged \"{1}\" but has no return annotation",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

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
    // no fix site to attach and no value to hand the fixer.
    public static void ReportMismatch(
        OperationAnalysisContext context,
        Location location,
        SyntaxInfo source,
        ISymbol? targetSymbol,
        SyntaxInfo target) =>
        context.ReportDiagnostic(Diagnostic.Create(
            formatMismatch,
            location,
            SyntaxValueMatcher.FormatValues(source.Values),
            DescribeSymbol(targetSymbol),
            SyntaxValueMatcher.FormatValues(target.Values)));

    // SSA002. `fixTarget` is the untagged source's declaration — the codefix adds a
    // [StringSyntax] there (or [ReturnSyntax] when it is a method) matching `target`.
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
            [DescribeSymbol(targetSymbol), SyntaxValueMatcher.FormatValues(target.Values)]));

    // SSA003. Mirror of SSA002: the target is both the fix site and the description
    // rendered into the message, and the tagged side is the source.
    public static void ReportDropped(
        OperationAnalysisContext context,
        Location location,
        ISymbol? targetSymbol,
        SyntaxInfo source) =>
        context.ReportDiagnostic(CreateFixable(
            droppedFormat,
            location,
            targetSymbol,
            source,
            [SyntaxValueMatcher.FormatValues(source.Values), DescribeSymbol(targetSymbol)]));

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
            SyntaxValueMatcher.FormatValues(left.Values),
            DescribeSymbol(rightSymbol),
            SyntaxValueMatcher.FormatValues(right.Values)));

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
                SyntaxValueMatcher.FormatValues(attributedInfo.Values),
                DescribeSymbol(bareSymbol)
            ]));

    // SSA006. The single option travels in the property bag so the fixer can rewrite
    // [UnionSyntax("X")] to [StringSyntax("X")] without re-parsing the attribute.
    public static void ReportSingletonUnion(
        SymbolAnalysisContext context,
        Location location,
        string singleValue) =>
        context.ReportDiagnostic(Diagnostic.Create(
            singletonUnion,
            location,
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, singleValue),
            messageArgs: singleValue));

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
            messageArgs: canonical));

    // SSA008. `conventionTarget` selects the fix shape: "Attribute" deletes the
    // annotation, "LanguageComment" strips the trivia. The comment's exact trivia is
    // re-resolved at fix time, so only the declaration location travels here.
    // Reported from two registrations — symbol actions for annotated declarations, a
    // syntax-node action for `//language=` comments on locals — and the two contexts
    // share no interface, hence the pair of overloads over one factory.
    public static void ReportRedundantByConvention(
        SymbolAnalysisContext context,
        Location location,
        string conventionValue,
        string conventionTarget) =>
        context.ReportDiagnostic(
            CreateRedundantByConvention(location, conventionValue, conventionTarget));

    public static void ReportRedundantByConvention(
        SyntaxNodeAnalysisContext context,
        Location location,
        string conventionValue,
        string conventionTarget) =>
        context.ReportDiagnostic(
            CreateRedundantByConvention(location, conventionValue, conventionTarget));

    static Diagnostic CreateRedundantByConvention(
        Location location,
        string conventionValue,
        string conventionTarget) =>
        Diagnostic.Create(
            redundantByConvention,
            location,
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(ValueKey, conventionValue)
                .Add(ConventionTargetKey, conventionTarget),
            messageArgs: conventionValue);

    // SSA009. Reported on the member's identifier so the squiggle is tight, with the
    // whole declaration carried alongside as the fix site.
    public static void ReportMissingReturnAnnotation(
        OperationBlockAnalysisContext context,
        Location identifierLocation,
        Location declarationLocation,
        string memberKind,
        string value) =>
        context.ReportDiagnostic(Diagnostic.Create(
            missingReturnAnnotation,
            identifierLocation,
            additionalLocations: [declarationLocation],
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, value),
            messageArgs: [memberKind, value]));

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
            // (one per value + one combined). The pipe is the same separator used in the
            // user-visible message — safe because values are identifier-like.
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(ValueKey, SyntaxValueMatcher.FormatValues(info.Values)),
            messageArgs: messageArgs);

    static Location[]? GetAdditionalLocations(ISymbol? fixTarget)
    {
        var declaration = fixTarget?.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return [Location.Create(declaration.SyntaxTree, declaration.Span)];
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

    static string QualifiedName(ISymbol symbol)
    {
        var type = symbol.ContainingType;
        return type is null ? symbol.Name : $"{type.Name}.{symbol.Name}";
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

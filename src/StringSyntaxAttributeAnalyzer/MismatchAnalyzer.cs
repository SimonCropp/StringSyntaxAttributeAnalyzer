namespace StringSyntaxAttributeAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
    const string valueKey = "StringSyntaxValue";

    // EditorConfig knob: comma-separated namespace patterns whose types, when they
    // appear as the *target* of a format-dropping flow, should be ignored. Defaults
    // cover the BCL since its APIs can't be attributed retroactively. Patterns support
    // a trailing `*` wildcard (prefix match). Example override:
    //   stringsyntax.suppressed_target_namespaces = System*,Microsoft*,MyCompany.Legacy*
    const string suppressedNamespacesKey = "stringsyntax.suppressed_target_namespaces";
    const string defaultSuppressedNamespaces = "System*,Microsoft*";

    static readonly DiagnosticDescriptor formatMismatchRule = new(
        id: "SSA001",
        title: "StringSyntax format mismatch",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to a target with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor missingSourceFormatRule = new(
        id: "SSA002",
        title: "Source has no StringSyntax while target requires one",
        messageFormat: "Value has no StringSyntax attribute but is assigned to a target with StringSyntax \"{0}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor droppedFormatRule = new(
        id: "SSA003",
        title: "Source has StringSyntax while target has none",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to a target without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMismatchRule = new(
        id: "SSA004",
        title: "Equality comparison between mismatched StringSyntax values",
        messageFormat: "Comparing a value with StringSyntax \"{0}\" to a value with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMissingFormatRule = new(
        id: "SSA005",
        title: "Equality comparison with an unattributed value",
        messageFormat: "Comparing a value with StringSyntax \"{0}\" to a value without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor singletonUnionRule = new(
        id: "SSA006",
        title: "UnionSyntax with a single option should be StringSyntax",
        messageFormat: "[UnionSyntax(\"{0}\")] has only one option; use [StringSyntax(\"{0}\")] instead",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        formatMismatchRule,
        missingSourceFormatRule,
        droppedFormatRule,
        equalityMismatchRule,
        equalityMissingFormatRule,
        singletonUnionRule
    ];

    public override void Initialize(AnalysisContext context)
    {
        context.ConfigureGeneratedCodeAnalysis(GeneratedCodeAnalysisFlags.None);
        context.EnableConcurrentExecution();

        context.RegisterCompilationStartAction(start =>
        {
            // Resolve StringSyntaxAttribute once per compilation. If the consumer doesn't
            // reference System.Diagnostics.CodeAnalysis.StringSyntaxAttribute at all, this
            // returns null and the analyzer stays dormant for this compilation.
            var stringSyntaxType = start.Compilation
                .GetTypeByMetadataName("System.Diagnostics.CodeAnalysis.StringSyntaxAttribute");
            if (stringSyntaxType is null)
            {
                return;
            }

            // UnionSyntaxAttribute is ours (source-generated, internal per-assembly). Null
            // if the generator hasn't run — in that case we just behave as before.
            var unionSyntaxType = start.Compilation
                .GetTypeByMetadataName("StringSyntaxAttributeAnalyzer.UnionSyntaxAttribute");
            // ReturnSyntaxAttribute fills the BCL gap (StringSyntaxAttribute can't target
            // return values). Also source-generated, also null if the generator hasn't run.
            // TODO: retire this lookup when https://github.com/dotnet/runtime/issues/76203
            // ships — StringSyntaxAttribute will then target Method/ReturnValue directly.
            var returnSyntaxType = start.Compilation
                .GetTypeByMetadataName("StringSyntaxAttributeAnalyzer.ReturnSyntaxAttribute");
            var types = new SyntaxTypes(stringSyntaxType, unionSyntaxType, returnSyntaxType);

            var suppressedNamespaces = ReadSuppressedNamespaces(start.Options);

            start.RegisterOperationAction(
                _ => AnalyzeArgument(_, types, suppressedNamespaces),
                OperationKind.Argument);
            start.RegisterOperationAction(
                _ => AnalyzeSimpleAssignment(_, types, suppressedNamespaces),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                _ => AnalyzePropertyInitializer(_, types, suppressedNamespaces),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeFieldInitializer(_, types, suppressedNamespaces),
                OperationKind.FieldInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeBinaryOperator(_, types, suppressedNamespaces),
                OperationKind.BinaryOperator);
            if (unionSyntaxType is not null)
            {
                start.RegisterSymbolAction(
                    _ => AnalyzeSymbolForSingletonUnion(_, unionSyntaxType),
                    SymbolKind.Parameter,
                    SymbolKind.Property,
                    SymbolKind.Field);
            }
        });
    }

    static string[] ReadSuppressedNamespaces(AnalyzerOptions options)
    {
        var raw = options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(suppressedNamespacesKey, out var configured)
            ? configured
            : defaultSuppressedNamespaces;

        return raw
            .Split(',')
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0)
            .ToArray();
    }

    static void AnalyzeArgument(
        OperationAnalysisContext context,
        SyntaxTypes types,
        string[] suppressedNamespaces)
    {
        var argument = (IArgumentOperation)context.Operation;
        var parameter = argument.Parameter;
        if (parameter is null)
        {
            return;
        }

        var targetInfo = GetSyntaxFromAttributes(parameter.GetAttributes(), types);
        var sourceSymbol = GetSymbol(argument.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        Report(
            context,
            argument.Value.Syntax.GetLocation(),
            sourceSymbol,
            sourceInfo,
            parameter,
            targetInfo,
            suppressedNamespaces);
    }

    static void AnalyzeSimpleAssignment(
        OperationAnalysisContext context,
        SyntaxTypes types,
        string[] suppressedNamespaces)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        var targetSymbol = GetSymbol(assignment.Target);
        if (targetSymbol is null)
        {
            return;
        }

        var targetInfo = GetSyntax(targetSymbol, types);
        var sourceSymbol = GetSymbol(assignment.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        Report(
            context,
            assignment.Value.Syntax.GetLocation(),
            sourceSymbol,
            sourceInfo,
            targetSymbol,
            targetInfo,
            suppressedNamespaces);
    }

    static void AnalyzePropertyInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        string[] suppressedNamespaces)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        foreach (var property in init.InitializedProperties)
        {
            var targetInfo = GetSyntax(property, types);
            Report(
                context,
                init.Value.Syntax.GetLocation(),
                sourceSymbol,
                sourceInfo,
                property,
                targetInfo,
                suppressedNamespaces);
        }
    }

    static void AnalyzeFieldInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        string[] suppressedNamespaces)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        foreach (var field in init.InitializedFields)
        {
            var targetInfo = GetSyntaxFromAttributes(field.GetAttributes(), types);
            Report(
                context,
                init.Value.Syntax.GetLocation(),
                sourceSymbol,
                sourceInfo,
                field,
                targetInfo,
                suppressedNamespaces);
        }
    }

    static void AnalyzeSymbolForSingletonUnion(SymbolAnalysisContext context, INamedTypeSymbol unionSyntaxType)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, unionSyntaxType))
            {
                continue;
            }

            var options = ExtractUnionOptions(attribute);
            if (options.Length > 1)
            {
                return;
            }

            var location = attribute.ApplicationSyntaxReference?
                .GetSyntax(context.CancellationToken)
                .GetLocation();
            if (location is null)
            {
                return;
            }

            var singleValue = options.Length == 1 ? options[0] : "";
            var properties = ImmutableDictionary<string, string?>.Empty.Add(valueKey, singleValue);
            context.ReportDiagnostic(Diagnostic.Create(
                singletonUnionRule,
                location,
                properties: properties,
                messageArgs: singleValue));
            return;
        }
    }

    static void AnalyzeBinaryOperator(
        OperationAnalysisContext context,
        SyntaxTypes types,
        string[] suppressedNamespaces)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var leftSymbol = GetSymbol(binary.LeftOperand);
        var rightSymbol = GetSymbol(binary.RightOperand);
        var leftInfo = GetSyntax(leftSymbol, types);
        var rightInfo = GetSyntax(rightSymbol, types);

        // Unknown side (literal, local, invocation) — suppress. Comparing to a literal is
        // common and fine; the analyzer can't infer intent from an opaque expression.
        if (leftInfo.State == SyntaxState.Unknown ||
            rightInfo.State == SyntaxState.Unknown)
        {
            return;
        }

        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.Present)
        {
            if (ValuesMatch(leftInfo.Values, rightInfo.Values))
            {
                return;
            }

            // Both sides have attributes, values differ — no codefix (picking which side
            // is wrong requires judgement, same reasoning as SSA001).
            context.ReportDiagnostic(Diagnostic.Create(
                equalityMismatchRule,
                binary.Syntax.GetLocation(),
                FormatValues(leftInfo.Values),
                FormatValues(rightInfo.Values)));
            return;
        }

        // One side Present, the other NotPresent — fixable: add the present side's
        // StringSyntax value to the bare side's declaration. Suppress when the bare side
        // is an object/T slot or lives in a suppressed namespace (BCL etc.).
        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(rightSymbol!)) ||
                IsInSuppressedNamespace(rightSymbol, suppressedNamespaces))
            {
                return;
            }

            context.ReportDiagnostic(CreateFixableDiagnostic(
                equalityMissingFormatRule,
                binary.Syntax.GetLocation(),
                rightSymbol,
                leftInfo.PrimaryValue));
        }
        else if (rightInfo.State == SyntaxState.Present &&
                 leftInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(leftSymbol!)) ||
                IsInSuppressedNamespace(leftSymbol, suppressedNamespaces))
            {
                return;
            }

            context.ReportDiagnostic(CreateFixableDiagnostic(
                equalityMissingFormatRule,
                binary.Syntax.GetLocation(),
                leftSymbol,
                rightInfo.PrimaryValue));
        }
    }

    // A "value slot" is a typed-object or generic-T target where strings flow as plain
    // values (logging, collections, generic extension methods). Passing a StringSyntax-
    // attributed value into such a slot doesn't meaningfully "drop" the format — the
    // receiver was never going to honour it. Skip SSA001/003/004/005 in those cases.
    static bool IsGenericValueSlot(ITypeSymbol? type) =>
        type is not null &&
        (type.SpecialType == SpecialType.System_Object ||
         type.TypeKind == TypeKind.TypeParameter ||
         (type is IArrayTypeSymbol array && IsGenericValueSlot(array.ElementType)));

    // Use OriginalDefinition so a generic method's `T value` parameter reads as TypeKind
    // TypeParameter even when the call site has substituted T=string.
    static ITypeSymbol? GetTargetType(ISymbol symbol) =>
        symbol.OriginalDefinition switch
        {
            IParameterSymbol p => p.Type,
            IPropertySymbol p => p.Type,
            IFieldSymbol f => f.Type,
            _ => null
        };

    static ISymbol? GetSymbol(IOperation operation)
    {
        operation = UnwrapConversions(operation);
        return operation switch
        {
            IPropertyReferenceOperation prop => prop.Property,
            IFieldReferenceOperation field => field.Field,
            IParameterReferenceOperation param => param.Parameter,
            IInvocationOperation invocation => invocation.TargetMethod,
            _ => null
        };
    }

    static SyntaxInfo GetSyntax(ISymbol? symbol, SyntaxTypes types)
    {
        if (symbol is null)
        {
            return SyntaxInfo.Unknown;
        }

        var info = GetSyntaxFromAttributes(symbol.GetAttributes(), types);
        if (info.State == SyntaxState.Present)
        {
            return info;
        }

        // Method invocations are only resolvable when the method opts in via
        // [ReturnSyntax]. Without it, treat the result as Unknown (not NotPresent) —
        // otherwise every helper returning a string would fire SSA002 at call sites.
        if (symbol is IMethodSymbol)
        {
            return SyntaxInfo.Unknown;
        }

        // Records: a primary-constructor parameter with [StringSyntax] doesn't
        // propagate the attribute to the synthesized property (the default
        // attribute target for such parameters is the parameter itself). Treat
        // the parameter's attribute as also applying to the generated property.
        if (symbol is IPropertySymbol property &&
            FindPrimaryConstructorParameter(property) is { } parameter)
        {
            return GetSyntaxFromAttributes(parameter.GetAttributes(), types);
        }

        return info;
    }

    static IParameterSymbol? FindPrimaryConstructorParameter(IPropertySymbol property)
    {
        var type = property.ContainingType;
        if (type is null || !type.IsRecord)
        {
            return null;
        }

        foreach (var constructor in type.InstanceConstructors)
        {
            foreach (var parameter in constructor.Parameters)
            {
                if (parameter.Name == property.Name &&
                    SymbolEqualityComparer.Default.Equals(parameter.Type, property.Type))
                {
                    return parameter;
                }
            }
        }

        return null;
    }

    static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    readonly struct SyntaxTypes(
        INamedTypeSymbol stringSyntax,
        INamedTypeSymbol? unionSyntax,
        INamedTypeSymbol? returnSyntax)
    {
        public INamedTypeSymbol StringSyntax { get; } = stringSyntax;
        public INamedTypeSymbol? UnionSyntax { get; } = unionSyntax;
        public INamedTypeSymbol? ReturnSyntax { get; } = returnSyntax;
    }

    static SyntaxInfo GetSyntaxFromAttributes(
        ImmutableArray<AttributeData> attributes,
        SyntaxTypes types)
    {
        foreach (var attribute in attributes)
        {
            if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.StringSyntax))
            {
                if (attribute.ConstructorArguments.Length > 0 &&
                    attribute.ConstructorArguments[0].Value is string s)
                {
                    return SyntaxInfo.Present(s);
                }
                return new(SyntaxState.Present, []);
            }

            if (types.UnionSyntax is not null &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.UnionSyntax))
            {
                var values = ExtractUnionOptions(attribute);
                return SyntaxInfo.PresentUnion(values);
            }

            if (types.ReturnSyntax is not null &&
                SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.ReturnSyntax))
            {
                if (attribute.ConstructorArguments.Length > 0 &&
                    attribute.ConstructorArguments[0].Value is string s)
                {
                    return SyntaxInfo.Present(s);
                }
                return new(SyntaxState.Present, []);
            }
        }

        return SyntaxInfo.NotPresent;
    }

    static ImmutableArray<string> ExtractUnionOptions(AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length == 0)
        {
            return [];
        }

        var first = attribute.ConstructorArguments[0];
        if (first.Kind != TypedConstantKind.Array)
        {
            return [];
        }

        var builder = ImmutableArray.CreateBuilder<string>(first.Values.Length);
        foreach (var element in first.Values)
        {
            if (element.Value is string s)
            {
                builder.Add(s);
            }
        }
        return builder.ToImmutable();
    }

    static void Report(
        OperationAnalysisContext context,
        Location location,
        ISymbol? sourceSymbol,
        SyntaxInfo source,
        ISymbol targetSymbol,
        SyntaxInfo target,
        string[] suppressedNamespaces)
    {
        if (source.State == SyntaxState.Unknown ||
            target.State == SyntaxState.Unknown)
        {
            return;
        }

        // Target is object/T/params object[] without StringSyntax — treat as a generic
        // value slot (log methods, collections, println, ToString-ers). The source's
        // format info isn't being meaningfully "dropped", so suppress SSA003/SSA001.
        if (target.State == SyntaxState.NotPresent &&
            IsGenericValueSlot(GetTargetType(targetSymbol)))
        {
            return;
        }

        if (source.State == SyntaxState.Present &&
            target.State == SyntaxState.Present)
        {
            if (!ValuesMatch(source.Values, target.Values))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    formatMismatchRule,
                    location,
                    FormatValues(source.Values),
                    FormatValues(target.Values)));
            }

            return;
        }

        if (source.State == SyntaxState.NotPresent &&
            target.State == SyntaxState.Present)
        {
            // Fix site is the source symbol's declaration (add StringSyntax matching target).
            context.ReportDiagnostic(
                CreateFixableDiagnostic(
                    missingSourceFormatRule,
                    location,
                    sourceSymbol,
                    target.PrimaryValue));
            return;
        }

        if (source.State == SyntaxState.Present &&
            target.State == SyntaxState.NotPresent)
        {
            // SSA003: the target can't be fixed if it's in a namespace the user can't
            // edit (BCL by default). Bail rather than showing an unfixable warning.
            if (IsInSuppressedNamespace(targetSymbol, suppressedNamespaces))
            {
                return;
            }

            // Fix site is the target symbol's declaration (add StringSyntax matching source).
            context.ReportDiagnostic(CreateFixableDiagnostic(
                droppedFormatRule,
                location,
                targetSymbol,
                source.PrimaryValue));
        }
    }

    static string FormatValues(ImmutableArray<string> values) =>
        values.IsDefaultOrEmpty ? "" : string.Join("|", values);

    static bool IsInSuppressedNamespace(ISymbol? symbol, string[] patterns)
    {
        if (symbol is null || patterns.Length == 0)
        {
            return false;
        }

        // For parameters, check the containing method's type's namespace; for
        // properties/fields, check the containing type's namespace.
        var owner = symbol.ContainingType ?? symbol.ContainingSymbol as INamedTypeSymbol;
        var ns = owner?.ContainingNamespace;
        if (ns is null || ns.IsGlobalNamespace)
        {
            return false;
        }

        var fullName = ns.ToDisplayString();
        foreach (var pattern in patterns)
        {
            if (pattern.Length == 0)
            {
                continue;
            }

            if (pattern[pattern.Length - 1] == '*')
            {
                var prefix = pattern.Substring(0, pattern.Length - 1);
                if (fullName.StartsWith(prefix, StringComparison.Ordinal))
                {
                    return true;
                }
            }
            else if (fullName == pattern)
            {
                return true;
            }
        }

        return false;
    }

    // Only the first character is compared case-insensitively — the BCL constants
    // (Regex, Json, Xml, DateTimeFormat, ...) are PascalCase, and we want "json" and
    // "Json" to be treated as the same format. "jSon" vs "json" is still a mismatch.
    // Two value-sets "match" if they overlap on at least one value. `[StringSyntax(x)]`
    // ↔ `[UnionSyntax(x, y)]` matches via `x`. Union ↔ union passes when the intersection
    // is non-empty.
    static bool ValuesMatch(ImmutableArray<string> a, ImmutableArray<string> b)
    {
        foreach (var va in a)
        {
            foreach (var vb in b)
            {
                if (SingleValueMatches(va, vb))
                {
                    return true;
                }
            }
        }
        return false;
    }

    // First character compared case-insensitively — the BCL constants (Regex, Json, Xml,
    // DateTimeFormat, ...) are PascalCase, and we want "json" and "Json" to be treated
    // as the same format. "jSon" vs "json" is still a mismatch.
    static bool SingleValueMatches(string? a, string? b)
    {
        if (ReferenceEquals(a, b))
        {
            return true;
        }

        if (a is null || b is null)
        {
            return false;
        }

        if (a.Length != b.Length)
        {
            return false;
        }

        if (a.Length == 0)
        {
            return true;
        }

        if (char.ToLowerInvariant(a[0]) != char.ToLowerInvariant(b[0]))
        {
            return false;
        }

        return string.CompareOrdinal(a, 1, b, 1, a.Length - 1) == 0;
    }

    static Diagnostic CreateFixableDiagnostic(
        DiagnosticDescriptor rule,
        Location location,
        ISymbol? fixTarget,
        string? value) =>
        Diagnostic.Create(
            rule,
            location,
            additionalLocations: GetAdditionalLocations(fixTarget),
            properties: ImmutableDictionary<string, string?>.Empty.Add(valueKey, value),
            messageArgs: value ?? "");

    static Location[]? GetAdditionalLocations(ISymbol? fixTarget)
    {
        var declaration = fixTarget?.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return [Location.Create(declaration.SyntaxTree, declaration.Span)];
    }

    enum SyntaxState
    {
        Unknown,
        NotPresent,
        Present
    }

    readonly struct SyntaxInfo(SyntaxState state, ImmutableArray<string> values)
    {
        public SyntaxState State { get; } = state;

        // The set of accepted syntax values. Single-valued for `[StringSyntax(x)]`,
        // multi-valued for `[UnionSyntax(a, b, c)]`. Empty when State != Present.
        public ImmutableArray<string> Values { get; } = values;

        // Primary value to surface in messages and codefixes. For a union, takes the
        // first option — consistent but arbitrary; consumers overriding to a specific
        // value is expected.
        public string? PrimaryValue => Values.IsDefaultOrEmpty ? null : Values[0];

        public static SyntaxInfo Unknown { get; } = new(SyntaxState.Unknown, []);
        public static SyntaxInfo NotPresent { get; } = new(SyntaxState.NotPresent, []);
        public static SyntaxInfo Present(string value) => new(SyntaxState.Present, [value]);
        public static SyntaxInfo PresentUnion(ImmutableArray<string> values) =>
            new(SyntaxState.Present, values);
    }
}

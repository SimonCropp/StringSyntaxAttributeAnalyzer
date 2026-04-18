namespace StringSyntaxAttributeAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
    public const string ValueKey = "StringSyntaxValue";

    // EditorConfig knob: comma-separated namespace patterns whose types, when they
    // appear as the *target* of a format-dropping flow, should be ignored. Defaults
    // cover the BCL since its APIs can't be attributed retroactively. Patterns support
    // a trailing `*` wildcard (prefix match). Example override:
    //   stringsyntax.suppressed_target_namespaces = System*,Microsoft*,MyCompany.Legacy*
    const string SuppressedNamespacesKey = "stringsyntax.suppressed_target_namespaces";
    const string DefaultSuppressedNamespaces = "System*,Microsoft*";

    public static readonly DiagnosticDescriptor FormatMismatchRule = new(
        id: "SSA001",
        title: "StringSyntax format mismatch",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to a target with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor MissingSourceFormatRule = new(
        id: "SSA002",
        title: "Source has no StringSyntax while target requires one",
        messageFormat: "Value has no StringSyntax attribute but is assigned to a target with StringSyntax \"{0}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor DroppedFormatRule = new(
        id: "SSA003",
        title: "Source has StringSyntax while target has none",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to a target without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EqualityMismatchRule = new(
        id: "SSA004",
        title: "Equality comparison between mismatched StringSyntax values",
        messageFormat: "Comparing a value with StringSyntax \"{0}\" to a value with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public static readonly DiagnosticDescriptor EqualityMissingFormatRule = new(
        id: "SSA005",
        title: "Equality comparison with an unattributed value",
        messageFormat: "Comparing a value with StringSyntax \"{0}\" to a value without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
    [
        FormatMismatchRule,
        MissingSourceFormatRule,
        DroppedFormatRule,
        EqualityMismatchRule,
        EqualityMissingFormatRule
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

            var suppressedNamespaces = ReadSuppressedNamespaces(start.Options);

            start.RegisterOperationAction(
                _ => AnalyzeArgument(_, stringSyntaxType, suppressedNamespaces),
                OperationKind.Argument);
            start.RegisterOperationAction(
                _ => AnalyzeSimpleAssignment(_, stringSyntaxType, suppressedNamespaces),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                _ => AnalyzePropertyInitializer(_, stringSyntaxType, suppressedNamespaces),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeFieldInitializer(_, stringSyntaxType, suppressedNamespaces),
                OperationKind.FieldInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeBinaryOperator(_, stringSyntaxType, suppressedNamespaces),
                OperationKind.BinaryOperator);
        });
    }

    static string[] ReadSuppressedNamespaces(AnalyzerOptions options)
    {
        var raw = options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(SuppressedNamespacesKey, out var configured)
            ? configured
            : DefaultSuppressedNamespaces;

        return raw
            .Split(',')
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0)
            .ToArray();
    }

    static void AnalyzeArgument(
        OperationAnalysisContext context,
        INamedTypeSymbol stringSyntaxType,
        string[] suppressedNamespaces)
    {
        var argument = (IArgumentOperation)context.Operation;
        var parameter = argument.Parameter;
        if (parameter is null)
        {
            return;
        }

        var targetInfo = GetSyntaxFromAttributes(parameter.GetAttributes(), stringSyntaxType);
        var sourceSymbol = GetSymbol(argument.Value);
        var sourceInfo = GetSyntax(sourceSymbol, stringSyntaxType);
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
        INamedTypeSymbol stringSyntaxType,
        string[] suppressedNamespaces)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        var targetSymbol = GetSymbol(assignment.Target);
        if (targetSymbol is null)
        {
            return;
        }

        var targetInfo = GetSyntax(targetSymbol, stringSyntaxType);
        var sourceSymbol = GetSymbol(assignment.Value);
        var sourceInfo = GetSyntax(sourceSymbol, stringSyntaxType);
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
        INamedTypeSymbol stringSyntaxType,
        string[] suppressedNamespaces)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, stringSyntaxType);
        foreach (var property in init.InitializedProperties)
        {
            var targetInfo = GetSyntaxFromAttributes(property.GetAttributes(), stringSyntaxType);
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
        INamedTypeSymbol stringSyntaxType,
        string[] suppressedNamespaces)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, stringSyntaxType);
        foreach (var field in init.InitializedFields)
        {
            var targetInfo = GetSyntaxFromAttributes(field.GetAttributes(), stringSyntaxType);
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

    static void AnalyzeBinaryOperator(
        OperationAnalysisContext context,
        INamedTypeSymbol stringSyntaxType,
        string[] suppressedNamespaces)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var leftSymbol = GetSymbol(binary.LeftOperand);
        var rightSymbol = GetSymbol(binary.RightOperand);
        var leftInfo = GetSyntax(leftSymbol, stringSyntaxType);
        var rightInfo = GetSyntax(rightSymbol, stringSyntaxType);

        // Unknown side (literal, local, invocation) — suppress. Comparing to a literal is
        // common and fine; the analyzer can't infer intent from an opaque expression.
        if (leftInfo.State == SyntaxState.Unknown || rightInfo.State == SyntaxState.Unknown)
        {
            return;
        }

        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.Present)
        {
            if (ValuesMatch(leftInfo.Value, rightInfo.Value))
            {
                return;
            }

            // Both sides have attributes, values differ — no codefix (picking which side
            // is wrong requires judgement, same reasoning as SSA001).
            context.ReportDiagnostic(Diagnostic.Create(
                EqualityMismatchRule,
                binary.Syntax.GetLocation(),
                leftInfo.Value ?? "",
                rightInfo.Value ?? ""));
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
                EqualityMissingFormatRule,
                binary.Syntax.GetLocation(),
                rightSymbol,
                leftInfo.Value));
        }
        else if (rightInfo.State == SyntaxState.Present && leftInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(leftSymbol!)) ||
                IsInSuppressedNamespace(leftSymbol, suppressedNamespaces))
            {
                return;
            }

            context.ReportDiagnostic(CreateFixableDiagnostic(
                EqualityMissingFormatRule,
                binary.Syntax.GetLocation(),
                leftSymbol,
                rightInfo.Value));
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
            _ => null
        };
    }

    static SyntaxInfo GetSyntax(ISymbol? symbol, INamedTypeSymbol stringSyntaxType) =>
        symbol is null
            ? SyntaxInfo.Unknown
            : GetSyntaxFromAttributes(symbol.GetAttributes(), stringSyntaxType);

    static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    static SyntaxInfo GetSyntaxFromAttributes(
        ImmutableArray<AttributeData> attributes,
        INamedTypeSymbol stringSyntaxType)
    {
        foreach (var attribute in attributes)
        {
            if (!SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, stringSyntaxType))
            {
                continue;
            }

            string? value = null;
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string s)
            {
                value = s;
            }

            return new(SyntaxState.Present, value);
        }

        return new(SyntaxState.NotPresent, null);
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
            if (!ValuesMatch(source.Value, target.Value))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    FormatMismatchRule,
                    location,
                    source.Value ?? "",
                    target.Value ?? ""));
            }

            return;
        }

        if (source.State == SyntaxState.NotPresent &&
            target.State == SyntaxState.Present)
        {
            // Fix site is the source symbol's declaration (add StringSyntax matching target).
            context.ReportDiagnostic(CreateFixableDiagnostic(
                MissingSourceFormatRule,
                location,
                sourceSymbol,
                target.Value));
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
                DroppedFormatRule,
                location,
                targetSymbol,
                source.Value));
        }
    }

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
    static bool ValuesMatch(string? a, string? b)
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
            properties: ImmutableDictionary<string, string?>.Empty.Add(ValueKey, value),
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

    readonly struct SyntaxInfo(SyntaxState state, string? value)
    {
        public SyntaxState State { get; } = state;
        public string? Value { get; } = value;

        public static SyntaxInfo Unknown => new(SyntaxState.Unknown, null);
    }
}

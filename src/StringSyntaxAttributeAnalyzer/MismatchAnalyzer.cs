namespace StringSyntaxAttributeAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
    public const string ValueKey = "StringSyntaxValue";

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

    public override ImmutableArray<DiagnosticDescriptor> SupportedDiagnostics =>
        [FormatMismatchRule, MissingSourceFormatRule, DroppedFormatRule];

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

            start.RegisterOperationAction(
                ctx => AnalyzeArgument(ctx, stringSyntaxType),
                OperationKind.Argument);
            start.RegisterOperationAction(
                ctx => AnalyzeSimpleAssignment(ctx, stringSyntaxType),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                ctx => AnalyzePropertyInitializer(ctx, stringSyntaxType),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                ctx => AnalyzeFieldInitializer(ctx, stringSyntaxType),
                OperationKind.FieldInitializer);
        });
    }

    static void AnalyzeArgument(OperationAnalysisContext context, INamedTypeSymbol stringSyntaxType)
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
            targetInfo);
    }

    static void AnalyzeSimpleAssignment(OperationAnalysisContext context, INamedTypeSymbol stringSyntaxType)
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
            targetInfo);
    }

    static void AnalyzePropertyInitializer(OperationAnalysisContext context, INamedTypeSymbol stringSyntaxType)
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
                targetInfo);
        }
    }

    static void AnalyzeFieldInitializer(OperationAnalysisContext context, INamedTypeSymbol stringSyntaxType)
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
                targetInfo);
        }
    }

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
        SyntaxInfo target)
    {
        if (source.State == SyntaxState.Unknown ||
            target.State == SyntaxState.Unknown)
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

        if (source.State == SyntaxState.NotPresent && target.State == SyntaxState.Present)
        {
            // Fix site is the source symbol's declaration (add StringSyntax matching target).
            context.ReportDiagnostic(CreateFixableDiagnostic(
                MissingSourceFormatRule,
                location,
                sourceSymbol,
                target.Value));
            return;
        }

        if (source.State == SyntaxState.Present && target.State == SyntaxState.NotPresent)
        {
            // Fix site is the target symbol's declaration (add StringSyntax matching source).
            context.ReportDiagnostic(CreateFixableDiagnostic(
                DroppedFormatRule,
                location,
                targetSymbol,
                source.Value));
        }
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
        string? value)
    {
        var declaration = fixTarget?.DeclaringSyntaxReferences.FirstOrDefault();
        var additionalLocations = declaration is null
            ? ImmutableArray<Location>.Empty
            : [Location.Create(declaration.SyntaxTree, declaration.Span)];

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(ValueKey, value);

        return Diagnostic.Create(
            rule,
            location,
            additionalLocations: additionalLocations,
            properties: properties,
            messageArgs: value ?? "");
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

namespace StringSyntaxAttributeAnalyzer;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
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

        context.RegisterOperationAction(AnalyzeArgument, OperationKind.Argument);
        context.RegisterOperationAction(AnalyzeSimpleAssignment, OperationKind.SimpleAssignment);
        context.RegisterOperationAction(AnalyzePropertyInitializer, OperationKind.PropertyInitializer);
        context.RegisterOperationAction(AnalyzeFieldInitializer, OperationKind.FieldInitializer);
    }

    static void AnalyzeArgument(OperationAnalysisContext context)
    {
        var argument = (IArgumentOperation)context.Operation;
        var parameter = argument.Parameter;
        if (parameter is null)
        {
            return;
        }

        var target = GetSyntaxFromAttributes(parameter.GetAttributes());
        var source = GetSourceSyntax(argument.Value);
        Report(context, argument.Value.Syntax.GetLocation(), source, target);
    }

    static void AnalyzeSimpleAssignment(OperationAnalysisContext context)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        var target = GetTargetSyntax(assignment.Target);
        if (target.State == SyntaxState.Unknown)
        {
            return;
        }

        var source = GetSourceSyntax(assignment.Value);
        Report(context, assignment.Value.Syntax.GetLocation(), source, target);
    }

    static void AnalyzePropertyInitializer(OperationAnalysisContext context)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var source = GetSourceSyntax(init.Value);
        foreach (var property in init.InitializedProperties)
        {
            var target = GetSyntaxFromAttributes(property.GetAttributes());
            Report(context, init.Value.Syntax.GetLocation(), source, target);
        }
    }

    static void AnalyzeFieldInitializer(OperationAnalysisContext context)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var source = GetSourceSyntax(init.Value);
        foreach (var field in init.InitializedFields)
        {
            var target = GetSyntaxFromAttributes(field.GetAttributes());
            Report(context, init.Value.Syntax.GetLocation(), source, target);
        }
    }

    static SyntaxInfo GetTargetSyntax(IOperation target)
    {
        target = UnwrapConversions(target);
        return target switch
        {
            IPropertyReferenceOperation prop => GetSyntaxFromAttributes(prop.Property.GetAttributes()),
            IFieldReferenceOperation field => GetSyntaxFromAttributes(field.Field.GetAttributes()),
            IParameterReferenceOperation param => GetSyntaxFromAttributes(param.Parameter.GetAttributes()),
            _ => SyntaxInfo.Unknown
        };
    }

    static SyntaxInfo GetSourceSyntax(IOperation value)
    {
        value = UnwrapConversions(value);
        return value switch
        {
            IPropertyReferenceOperation prop => GetSyntaxFromAttributes(prop.Property.GetAttributes()),
            IFieldReferenceOperation field => GetSyntaxFromAttributes(field.Field.GetAttributes()),
            IParameterReferenceOperation param => GetSyntaxFromAttributes(param.Parameter.GetAttributes()),
            _ => SyntaxInfo.Unknown
        };
    }

    static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }
        return operation;
    }

    static SyntaxInfo GetSyntaxFromAttributes(ImmutableArray<AttributeData> attributes)
    {
        foreach (var attribute in attributes)
        {
            var attrClass = attribute.AttributeClass;
            if (attrClass is null)
            {
                continue;
            }

            if (attrClass.Name != "StringSyntaxAttribute")
            {
                continue;
            }

            if (attrClass.ContainingNamespace?.ToDisplayString() != "System.Diagnostics.CodeAnalysis")
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
        SyntaxInfo source,
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
            if (!string.Equals(source.Value, target.Value, StringComparison.Ordinal))
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
            context.ReportDiagnostic(Diagnostic.Create(
                MissingSourceFormatRule,
                location,
                target.Value ?? ""));
            return;
        }

        if (source.State == SyntaxState.Present && target.State == SyntaxState.NotPresent)
        {
            context.ReportDiagnostic(Diagnostic.Create(
                DroppedFormatRule,
                location,
                source.Value ?? ""));
        }
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

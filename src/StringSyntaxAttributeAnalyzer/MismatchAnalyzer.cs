[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
    const string valueKey = "StringSyntaxValue";

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

            // UnionSyntaxAttribute and ReturnSyntaxAttribute are source-generated as
            // internal per-assembly, so the same metadata name can resolve to distinct
            // symbols in different compilations (and GetTypeByMetadataName returns null
            // when multiple references define it). Match by fully-qualified name
            // instead of symbol identity so cross-assembly attribute use still works.
            var types = new SyntaxTypes(stringSyntaxType);

            var suppression = new NamespaceSuppression(start.Options);

            start.RegisterOperationAction(
                _ => AnalyzeArgument(_, types, suppression),
                OperationKind.Argument);
            start.RegisterOperationAction(
                _ => AnalyzeSimpleAssignment(_, types, suppression),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                _ => AnalyzePropertyInitializer(_, types, suppression),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeFieldInitializer(_, types, suppression),
                OperationKind.FieldInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeBinaryOperator(_, types, suppression),
                OperationKind.BinaryOperator);
            start.RegisterSymbolAction(
                AnalyzeSymbolForSingletonUnion,
                SymbolKind.Parameter,
                SymbolKind.Property,
                SymbolKind.Field);
        });
    }

    static void AnalyzeArgument(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression)
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
            suppression.GetPatterns(context.Operation.Syntax.SyntaxTree));
    }

    static void AnalyzeSimpleAssignment(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression)
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
            suppression.GetPatterns(context.Operation.Syntax.SyntaxTree));
    }

    static void AnalyzePropertyInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        var patterns = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
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
                patterns);
        }
    }

    static void AnalyzeFieldInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types);
        var patterns = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
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
                patterns);
        }
    }

    static void AnalyzeSymbolForSingletonUnion(SymbolAnalysisContext context)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!IsAttributeNamed(attribute, unionSyntaxMetadataName))
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
        NamespaceSuppression suppression)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var suppressedNamespaces = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);

        var leftSymbol = GetSymbol(binary.LeftOperand);
        var rightSymbol = GetSymbol(binary.RightOperand);
        var leftInfo = GetSyntax(leftSymbol, types);
        var rightInfo = GetSyntax(rightSymbol, types);

        // Unknown side (literal, local, concatenation, await) — suppress. Comparing to
        // a literal is common and fine; the analyzer can't infer intent from an opaque
        // expression. Method invocations are NotPresent (fixable via [ReturnSyntax]),
        // not Unknown.
        if (leftInfo.State == SyntaxState.Unknown ||
            rightInfo.State == SyntaxState.Unknown)
        {
            return;
        }

        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.Present)
        {
            if (SyntaxValueMatcher.ValuesMatch(leftInfo.Values, rightInfo.Values))
            {
                return;
            }

            // Both sides have attributes, values differ — no codefix (picking which side
            // is wrong requires judgement, same reasoning as SSA001).
            context.ReportDiagnostic(Diagnostic.Create(
                equalityMismatchRule,
                binary.Syntax.GetLocation(),
                SyntaxValueMatcher.FormatValues(leftInfo.Values),
                SyntaxValueMatcher.FormatValues(rightInfo.Values)));
            return;
        }

        // One side Present, the other NotPresent — fixable: add the present side's
        // StringSyntax value to the bare side's declaration. Suppress when the bare side
        // is an object/T slot or lives in a suppressed namespace (BCL etc.).
        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(rightSymbol!)) ||
                NamespaceSuppression.Matches(rightSymbol, suppressedNamespaces))
            {
                return;
            }

            context.ReportDiagnostic(CreateFixableDiagnostic(
                equalityMissingFormatRule,
                binary.Syntax.GetLocation(),
                rightSymbol,
                leftInfo));
        }
        else if (rightInfo.State == SyntaxState.Present &&
                 leftInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(leftSymbol!)) ||
                NamespaceSuppression.Matches(leftSymbol, suppressedNamespaces))
            {
                return;
            }

            context.ReportDiagnostic(CreateFixableDiagnostic(
                equalityMissingFormatRule,
                binary.Syntax.GetLocation(),
                leftSymbol,
                rightInfo));
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
            // Generic methods returning T can't be usefully attributed with [ReturnSyntax]
            // — the annotation would apply to every substitution, not just string. Treat
            // as null so GetSyntax maps the source to Unknown and suppresses SSA002/003.
            IInvocationOperation invocation
                when invocation.TargetMethod.OriginalDefinition.ReturnType.TypeKind != TypeKind.TypeParameter
                => invocation.TargetMethod,
            ILocalReferenceOperation local => local.Local,
            _ => null
        };
    }

    static SyntaxInfo GetSyntax(ISymbol? symbol, SyntaxTypes types)
    {
        if (symbol is null)
        {
            return SyntaxInfo.Unknown;
        }

        // Locals can't carry attributes in C#. Instead we honour the JetBrains / Rider
        // `//language=<name>` comment convention on the declaration (or inline before
        // the initializer): an annotated local is Present, an unannotated local falls
        // through to NotPresent so SSA002 fires with a codefix that inserts the comment.
        // Doc: https://www.jetbrains.com/help/rider/Language_Injections.html
        if (symbol is ILocalSymbol local)
        {
            if (LanguageCommentReader.TryRead(local, out var comment))
            {
                return SyntaxInfo.Present(comment);
            }

            return SyntaxInfo.NotPresent;
        }

        var info = GetSyntaxFromAttributes(symbol.GetAttributes(), types);
        if (info.State == SyntaxState.Present)
        {
            return info;
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

    const string unionSyntaxMetadataName = "StringSyntaxAttributeAnalyzer.UnionSyntaxAttribute";
    const string returnSyntaxMetadataName = "StringSyntaxAttributeAnalyzer.ReturnSyntaxAttribute";

    readonly struct SyntaxTypes(INamedTypeSymbol stringSyntax)
    {
        public INamedTypeSymbol StringSyntax { get; } = stringSyntax;
    }

    static bool IsAttributeNamed(AttributeData attribute, string fullMetadataName)
    {
        var type = attribute.AttributeClass;
        if (type is null)
        {
            return false;
        }

        // ToDisplayString with the fully-qualified format yields "Namespace.TypeName"
        // for non-generic types, which matches our metadata-name constants.
        return type.ToDisplayString(SymbolDisplayFormat.FullyQualifiedFormat)
                   .Equals("global::" + fullMetadataName, StringComparison.Ordinal);
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

            if (IsAttributeNamed(attribute, unionSyntaxMetadataName))
            {
                var values = ExtractUnionOptions(attribute);
                return SyntaxInfo.PresentUnion(values);
            }

            if (IsAttributeNamed(attribute, returnSyntaxMetadataName))
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
            if (!SyntaxValueMatcher.ValuesMatch(source.Values, target.Values))
            {
                context.ReportDiagnostic(Diagnostic.Create(
                    formatMismatchRule,
                    location,
                    SyntaxValueMatcher.FormatValues(source.Values),
                    SyntaxValueMatcher.FormatValues(target.Values)));
            }

            return;
        }

        if (source.State == SyntaxState.NotPresent &&
            target.State == SyntaxState.Present)
        {
            // Source in a suppressed namespace (BCL etc.) can't be annotated by the
            // consumer — skip rather than surfacing an unfixable SSA002. Mirrors the
            // target-side check on the SSA003 branch below.
            if (NamespaceSuppression.Matches(sourceSymbol, suppressedNamespaces))
            {
                return;
            }

            // Fix site is the source symbol's declaration: add [StringSyntax] to a
            // field/property/parameter, or [ReturnSyntax] to a method.
            context.ReportDiagnostic(
                CreateFixableDiagnostic(
                    missingSourceFormatRule,
                    location,
                    sourceSymbol,
                    target));
            return;
        }

        if (source.State == SyntaxState.Present &&
            target.State == SyntaxState.NotPresent)
        {
            // SSA003: the target can't be fixed if it's in a namespace the user can't
            // edit (BCL by default). Bail rather than showing an unfixable warning.
            if (NamespaceSuppression.Matches(targetSymbol, suppressedNamespaces))
            {
                return;
            }

            // Fix site is the target symbol's declaration (add StringSyntax matching source).
            context.ReportDiagnostic(
                CreateFixableDiagnostic(
                    droppedFormatRule,
                    location,
                    targetSymbol,
                    source));
        }
    }

    static Diagnostic CreateFixableDiagnostic(
        DiagnosticDescriptor rule,
        Location location,
        ISymbol? fixTarget,
        SyntaxInfo info) =>
        Diagnostic.Create(
            rule,
            location,
            additionalLocations: GetAdditionalLocations(fixTarget),
            // Pipe-delimited so a UnionSyntax source can drive multiple codefix options
            // (one per value + one combined). The pipe is the same separator used in the
            // user-visible message — safe because values are identifier-like.
            properties: ImmutableDictionary<string, string?>.Empty.Add(valueKey, SyntaxValueMatcher.FormatValues(info.Values)),
            messageArgs: SyntaxValueMatcher.FormatValues(info.Values));

    static Location[]? GetAdditionalLocations(ISymbol? fixTarget)
    {
        var declaration = fixTarget?.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return [Location.Create(declaration.SyntaxTree, declaration.Span)];
    }
}
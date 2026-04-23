using System.Diagnostics.CodeAnalysis;

[DiagnosticAnalyzer(LanguageNames.CSharp)]
public class MismatchAnalyzer : DiagnosticAnalyzer
{
    const string valueKey = "StringSyntaxValue";

    static readonly DiagnosticDescriptor formatMismatchRule = new(
        id: "SSA001",
        title: "StringSyntax format mismatch",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to {1} with StringSyntax \"{2}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor missingSourceFormatRule = new(
        id: "SSA002",
        title: "Source has no StringSyntax while target requires one",
        messageFormat: "Value has no StringSyntax attribute but is assigned to {0} with StringSyntax \"{1}\"",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor droppedFormatRule = new(
        id: "SSA003",
        title: "Source has StringSyntax while target has none",
        messageFormat: "Value with StringSyntax \"{0}\" is assigned to {1} without a StringSyntax attribute",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMismatchRule = new(
        id: "SSA004",
        title: "Equality comparison between mismatched StringSyntax values",
        messageFormat: "Comparing {0} (StringSyntax \"{1}\") to {2} (StringSyntax \"{3}\")",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor equalityMissingFormatRule = new(
        id: "SSA005",
        title: "Equality comparison with an unattributed value",
        messageFormat: "Comparing {0} (StringSyntax \"{1}\") to {2} without a StringSyntax attribute",
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

    static readonly DiagnosticDescriptor redundantStringSyntaxRule = new(
        id: "SSA007",
        title: "StringSyntax can be replaced with a shortcut attribute",
        messageFormat: "[StringSyntax(\"{0}\")] can be replaced with [{0}]",
        category: "StringSyntaxAttribute.Usage",
        defaultSeverity: DiagnosticSeverity.Warning,
        isEnabledByDefault: true);

    static readonly DiagnosticDescriptor redundantByConventionRule = new(
        id: "SSA008",
        title: "StringSyntax annotation is redundant due to a name convention",
        messageFormat: "Annotation \"{0}\" is redundant: the name already matches the {0} convention",
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
        singletonUnionRule,
        redundantStringSyntaxRule,
        redundantByConventionRule
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

            // Shortcut attributes present in this compilation (empty unless the consumer
            // opted in with `StringSyntaxAnalyzer_EmitShortcutAttributes=true`). Used by
            // SSA007 to offer `[Html]` in place of `[StringSyntax("Html")]`. Keyed by
            // first-char-folded name so `"html"` also resolves to `Html`, matching the
            // case folding used elsewhere (SyntaxValueMatcher, KnownSyntaxConstants).
            var availableShortcuts = shortcutAttributeNames
                .Where(_ => start.Compilation
                    .GetTypeByMetadataName($"{shortcutAttributeNamespace}.{_}Attribute") is not null)
                .ToImmutableDictionary(FoldShortcutKey, _ => _);

            var suppression = new NamespaceSuppression(start.Options);
            var conventions = new NameConventionsOption(start.Options);
            var linqFlow = new LinqFlow();

            start.RegisterOperationAction(
                _ => AnalyzeArgument(_, types, suppression, conventions, linqFlow),
                OperationKind.Argument);
            start.RegisterOperationAction(
                _ => AnalyzeSimpleAssignment(_, types, suppression, conventions, linqFlow),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                _ => AnalyzePropertyInitializer(_, types, suppression, conventions, linqFlow),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeFieldInitializer(_, types, suppression, conventions, linqFlow),
                OperationKind.FieldInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeBinaryOperator(_, types, suppression, conventions, linqFlow),
                OperationKind.BinaryOperator);
            start.RegisterOperationAction(
                _ => AnalyzeLoop(_, types, linqFlow),
                OperationKind.Loop);
            start.RegisterSymbolAction(
                AnalyzeSymbolForSingletonUnion,
                SymbolKind.Parameter,
                SymbolKind.Property,
                SymbolKind.Field);

            if (availableShortcuts.Count > 0)
            {
                start.RegisterSymbolAction(
                    _ => AnalyzeSymbolForRedundantStringSyntax(_, types, availableShortcuts),
                    SymbolKind.Parameter,
                    SymbolKind.Property,
                    SymbolKind.Field,
                    SymbolKind.Method);
            }

            // SSA008: a name-convention match makes the symbol's StringSyntax (or
            // shortcut / single-value Return/Union) annotation redundant. Only fires
            // when the consumer opts in via `stringsyntax.name_conventions`. Methods
            // and return values are excluded by design — the convention applies to
            // member/local *names* (a method name like `GetUrl` doesn't carry the
            // url through the return value).
            start.RegisterSymbolAction(
                _ => AnalyzeSymbolForRedundantByConvention(_, types, conventions),
                SymbolKind.Parameter,
                SymbolKind.Property,
                SymbolKind.Field);

            // Locals can't be visited via SymbolAction, and `//language=` lives in
            // trivia rather than on the symbol — handle them via a syntax-node
            // walker over local declarations.
            start.RegisterSyntaxNodeAction(
                _ => AnalyzeLocalForRedundantByConvention(_, conventions),
                SyntaxKind.LocalDeclarationStatement);
        });
    }

    static void AnalyzeSymbolForRedundantStringSyntax(
        SymbolAnalysisContext context,
        SyntaxTypes types,
        ImmutableDictionary<string, string> availableShortcuts)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            TryReportRedundant(context, types, availableShortcuts, attribute);
        }

        // A `[StringSyntax("X")]` at `[return: ...]` position lives on return-value
        // attributes, not method-target attributes — sweep those too so SSA007 covers
        // the `[return: StringSyntax(...)]` case. `[ReturnSyntax("X")]` itself is
        // already covered by the method-target loop above.
        if (context.Symbol is IMethodSymbol method)
        {
            foreach (var attribute in method.GetReturnTypeAttributes())
            {
                TryReportRedundant(context, types, availableShortcuts, attribute);
            }
        }
    }

    static void TryReportRedundant(
        SymbolAnalysisContext context,
        SyntaxTypes types,
        ImmutableDictionary<string, string> availableShortcuts,
        AttributeData attribute)
    {
        string value;
        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.StringSyntax))
        {
            if (attribute.ConstructorArguments.Length == 0 ||
                attribute.ConstructorArguments[0].Value is not string s)
            {
                return;
            }
            value = s;
        }
        else if (IsAttributeNamed(attribute, returnSyntaxAttributeName))
        {
            // Only the single-value ReturnSyntax case maps to a shortcut. Multi-value
            // unions can't collapse to a single `[return: X]`.
            var options = ExtractUnionOptions(attribute);
            if (options.Length != 1)
            {
                return;
            }
            value = options[0];
        }
        else
        {
            return;
        }

        // First-char-folded lookup so `"html"` and `"Html"` both resolve to the
        // canonical `Html` shortcut. The canonical form is what we report in the
        // message and put in the properties bag, so the codefix emits `[Html]`.
        if (!availableShortcuts.TryGetValue(FoldShortcutKey(value), out var canonical))
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

        var properties = ImmutableDictionary<string, string?>.Empty.Add(valueKey, canonical);
        context.ReportDiagnostic(Diagnostic.Create(
            redundantStringSyntaxRule,
            location,
            properties: properties,
            messageArgs: canonical));
    }

    static string FoldShortcutKey(string name) =>
        name.Length == 0 ? name : char.ToLowerInvariant(name[0]) + name.Substring(1);

    static void AnalyzeSymbolForRedundantByConvention(
        SymbolAnalysisContext context,
        SyntaxTypes types,
        NameConventionsOption conventions)
    {
        if (!NameConventions.TryMatch(context.Symbol.Name, out var conventionValue))
        {
            return;
        }

        // Per-tree opt-in is keyed off the symbol's declaration tree. If the symbol
        // has no syntax (metadata-only) there's nothing to fix anyway.
        var declaration = context.Symbol.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return;
        }

        // Multi-declarator fields (`[StringSyntax("Uri")] string url, html;`)
        // share the attribute across all declarators. SSA008 would only fire for
        // the declarator whose name matches the attribute value, and removing
        // the shared attribute silently changes what the unflagged declarator
        // means. Skip, matching the multi-declarator-local policy in
        // AnalyzeLocalForRedundantByConvention.
        if (context.Symbol is IFieldSymbol &&
            declaration.GetSyntax(context.CancellationToken)
                is VariableDeclaratorSyntax { Parent: VariableDeclarationSyntax { Variables.Count: > 1 } })
        {
            return;
        }

        var conventionsEnabled = conventions.IsEnabled(declaration.SyntaxTree);

        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!TryGetSingleSyntaxValue(attribute, types, out var value) ||
                !SyntaxValueMatcher.SingleValuesMatch(value, conventionValue))
            {
                continue;
            }

            // Shortcut attributes (`[Html]`, `[Json]`, ...) only exist when the
            // consumer opted in via `StringSyntaxAnalyzer_EmitShortcutAttributes=true`
            // — that opt-in is itself enough to warrant SSA008 when the shortcut's
            // name matches the symbol's name convention. For plain `[StringSyntax]`
            // / `[ReturnSyntax]` / `[UnionSyntax]` we still require the broader
            // `name_conventions` opt-in, since those attributes may be intentional
            // even when the name happens to match.
            if (!conventionsEnabled && !TryMatchShortcutAttribute(attribute, out _))
            {
                continue;
            }

            ReportRedundantByConvention(context, attribute, conventionValue);
        }
    }

    static void AnalyzeLocalForRedundantByConvention(
        SyntaxNodeAnalysisContext context,
        NameConventionsOption conventions)
    {
        if (!conventions.IsEnabled(context.Node.SyntaxTree))
        {
            return;
        }

        var declaration = (LocalDeclarationStatementSyntax)context.Node;

        // Multi-declarator locals share trivia and a single comment maps to all of
        // them — too risky to remove. Skip; SSA008 won't fire for those.
        if (declaration.Declaration.Variables.Count != 1)
        {
            return;
        }

        var variable = declaration.Declaration.Variables[0];
        if (!NameConventions.TryMatch(variable.Identifier.ValueText, out var conventionValue))
        {
            return;
        }

        if (context.SemanticModel.GetDeclaredSymbol(variable, context.CancellationToken)
                is not ILocalSymbol localSymbol ||
            !LanguageCommentReader.TryRead(localSymbol, out var commentValue))
        {
            return;
        }

        if (!SyntaxValueMatcher.SingleValuesMatch(commentValue, conventionValue))
        {
            return;
        }

        // The fix removes the language comment; the location pinpoints the local
        // declaration so the codefix can locate it. The actual comment trivia is
        // resolved at fix time by re-running the trivia walk.
        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(valueKey, conventionValue)
            .Add("ConventionTarget", "LanguageComment");
        context.ReportDiagnostic(Diagnostic.Create(
            redundantByConventionRule,
            declaration.GetLocation(),
            properties: properties,
            messageArgs: conventionValue));
    }

    static void ReportRedundantByConvention(
        SymbolAnalysisContext context,
        AttributeData attribute,
        string conventionValue)
    {
        var location = attribute.ApplicationSyntaxReference?
            .GetSyntax(context.CancellationToken)
            .GetLocation();
        if (location is null)
        {
            return;
        }

        var properties = ImmutableDictionary<string, string?>.Empty
            .Add(valueKey, conventionValue)
            .Add("ConventionTarget", "Attribute");
        context.ReportDiagnostic(Diagnostic.Create(
            redundantByConventionRule,
            location,
            properties: properties,
            messageArgs: conventionValue));
    }

    // For SSA008 we only consider single-valued annotations. UnionSyntax with
    // multiple options can't be replaced by a single name convention without
    // dropping the other values.
    static bool TryGetSingleSyntaxValue(
        AttributeData attribute,
        SyntaxTypes types,
        [NotNullWhen(true)] out string? value)
    {
        value = null;
        if (SymbolEqualityComparer.Default.Equals(attribute.AttributeClass, types.StringSyntax))
        {
            if (attribute.ConstructorArguments.Length > 0 &&
                attribute.ConstructorArguments[0].Value is string s)
            {
                value = s;
                return true;
            }
            return false;
        }

        if (IsAttributeNamed(attribute, unionSyntaxAttributeName) ||
            IsAttributeNamed(attribute, returnSyntaxAttributeName))
        {
            var options = ExtractUnionOptions(attribute);
            if (options.Length == 1)
            {
                value = options[0];
                return true;
            }
            return false;
        }

        if (TryMatchShortcutAttribute(attribute, out var shortcut))
        {
            value = shortcut;
            return true;
        }

        return false;
    }

    static void AnalyzeArgument(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression,
        NameConventionsOption conventions,
        LinqFlow linqFlow)
    {
        var argument = (IArgumentOperation)context.Operation;
        var parameter = argument.Parameter;
        if (parameter is null)
        {
            return;
        }

        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var targetInfo = GetSyntaxFromAttributes(parameter.GetAttributes(), types);
        targetInfo = ApplyConvention(targetInfo, parameter, conventionsEnabled);
        var (sourceSymbol, sourceInfo) = GetSourceInfo(argument.Value, types, linqFlow, conventionsEnabled);
        Report(
            context,
            argument.Value.Syntax.GetLocation(),
            sourceSymbol,
            sourceInfo,
            parameter,
            targetInfo,
            suppression,
            suppression.GetPatterns(context.Operation.Syntax.SyntaxTree));
    }

    static void AnalyzeSimpleAssignment(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression,
        NameConventionsOption conventions,
        LinqFlow linqFlow)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        var targetSymbol = GetSymbol(assignment.Target);
        if (targetSymbol is null)
        {
            // Anonymous-type member targets carry no attribute surface, but the
            // author can opt into validation by writing `//language=X` on the
            // member initializer — treat that comment as the target tag and run
            // the normal mismatch / missing-source checks.
            if (assignment.Target is IPropertyReferenceOperation
                {
                    Property: { ContainingType.IsAnonymousType: true } anonProperty
                } &&
                LanguageCommentReader.TryReadFromNode(assignment.Syntax, out var anonValue))
            {
                var anonInfo = BuildCommentInfo(anonValue);
                var conventionsEnabledAnon = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
                var (anonSourceSymbol, anonSourceInfo) = GetSourceInfo(
                    assignment.Value, types, linqFlow, conventionsEnabledAnon);
                Report(
                    context,
                    assignment.Value.Syntax.GetLocation(),
                    anonSourceSymbol,
                    anonSourceInfo,
                    anonProperty,
                    anonInfo,
                    suppression,
                    suppression.GetPatterns(context.Operation.Syntax.SyntaxTree));
            }

            return;
        }

        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var targetInfo = GetSyntax(targetSymbol, types, conventionsEnabled);
        var (sourceSymbol, sourceInfo) = GetSourceInfo(assignment.Value, types, linqFlow, conventionsEnabled);
        Report(
            context,
            assignment.Value.Syntax.GetLocation(),
            sourceSymbol,
            sourceInfo,
            targetSymbol,
            targetInfo,
            suppression,
            suppression.GetPatterns(context.Operation.Syntax.SyntaxTree));
    }

    static void AnalyzePropertyInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression,
        NameConventionsOption conventions,
        LinqFlow linqFlow)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var (sourceSymbol, sourceInfo) = GetSourceInfo(init.Value, types, linqFlow, conventionsEnabled);
        var patterns = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
        foreach (var property in init.InitializedProperties)
        {
            // Anonymous-type properties can't host StringSyntax attributes —
            // firing SSA002/003 on `new { _.Tagged }` projections would be
            // unfixable. Tag propagation is handled on the read side via
            // GetSourceInfo/GetSymbol instead.
            if (property.ContainingType?.IsAnonymousType == true)
            {
                continue;
            }

            var targetInfo = GetSyntax(property, types, conventionsEnabled);
            Report(
                context,
                init.Value.Syntax.GetLocation(),
                sourceSymbol,
                sourceInfo,
                property,
                targetInfo,
                suppression,
                patterns);
        }
    }

    static void AnalyzeFieldInitializer(
        OperationAnalysisContext context,
        SyntaxTypes types,
        NamespaceSuppression suppression,
        NameConventionsOption conventions,
        LinqFlow linqFlow)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var (sourceSymbol, sourceInfo) = GetSourceInfo(init.Value, types, linqFlow, conventionsEnabled);
        var patterns = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
        foreach (var field in init.InitializedFields)
        {
            var targetInfo = ApplyConvention(
                GetSyntaxFromAttributes(field.GetAttributes(), types),
                field,
                conventionsEnabled);
            Report(
                context,
                init.Value.Syntax.GetLocation(),
                sourceSymbol,
                sourceInfo,
                field,
                targetInfo,
                suppression,
                patterns);
        }
    }

    static void AnalyzeSymbolForSingletonUnion(SymbolAnalysisContext context)
    {
        foreach (var attribute in context.Symbol.GetAttributes())
        {
            if (!IsAttributeNamed(attribute, unionSyntaxAttributeName))
            {
                continue;
            }

            var options = ExtractUnionOptions(attribute);
            // Only the exact singleton case is redundant. Empty `[UnionSyntax()]`
            // is a different shape of user error — "has only one option" would
            // lie, and the codefix would emit `[StringSyntax("")]`, which is
            // worse than staying silent.
            if (options.Length != 1)
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

            var singleValue = options[0];
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
        NamespaceSuppression suppression,
        NameConventionsOption conventions,
        LinqFlow linqFlow)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var suppressedNamespaces = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);

        var (leftSymbol, leftInfo) = GetSourceInfo(binary.LeftOperand, types, linqFlow, conventionsEnabled);
        var (rightSymbol, rightInfo) = GetSourceInfo(binary.RightOperand, types, linqFlow, conventionsEnabled);

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
                DescribeSymbol(leftSymbol),
                SyntaxValueMatcher.FormatValues(leftInfo.Values),
                DescribeSymbol(rightSymbol),
                SyntaxValueMatcher.FormatValues(rightInfo.Values)));
            return;
        }

        // One side Present, the other NotPresent — fixable: add the present side's
        // StringSyntax value to the bare side's declaration. Suppress when the bare side
        // is an object/T slot or lives in a suppressed namespace (BCL etc.).
        if (leftInfo.State == SyntaxState.Present && rightInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(rightSymbol!)) ||
                suppression.Matches(rightSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(rightSymbol) ||
                NameConventionMatches(rightSymbol, leftInfo.Values))
            {
                return;
            }

            context.ReportDiagnostic(CreateEqualityMissingDiagnostic(
                binary.Syntax.GetLocation(),
                attributedSymbol: leftSymbol,
                attributedInfo: leftInfo,
                bareSymbol: rightSymbol));
        }
        else if (rightInfo.State == SyntaxState.Present &&
                 leftInfo.State == SyntaxState.NotPresent)
        {
            if (IsGenericValueSlot(GetTargetType(leftSymbol!)) ||
                suppression.Matches(leftSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(leftSymbol) ||
                NameConventionMatches(leftSymbol, rightInfo.Values))
            {
                return;
            }

            context.ReportDiagnostic(CreateEqualityMissingDiagnostic(
                binary.Syntax.GetLocation(),
                attributedSymbol: rightSymbol,
                attributedInfo: rightInfo,
                bareSymbol: leftSymbol));
        }
    }

    // SSA005 has two descriptions to render ({attributed, bare}) instead of
    // SSA002/003's one, so it uses a dedicated factory rather than extending
    // CreateFixableDiagnostic's message-arg switch.
    static Diagnostic CreateEqualityMissingDiagnostic(
        Location location,
        ISymbol? attributedSymbol,
        SyntaxInfo attributedInfo,
        ISymbol? bareSymbol) =>
        Diagnostic.Create(
            equalityMissingFormatRule,
            location,
            additionalLocations: GetAdditionalLocations(bareSymbol),
            properties: ImmutableDictionary<string, string?>.Empty
                .Add(valueKey, SyntaxValueMatcher.FormatValues(attributedInfo.Values)),
            DescribeSymbol(attributedSymbol),
            SyntaxValueMatcher.FormatValues(attributedInfo.Values),
            DescribeSymbol(bareSymbol));

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
            // Anonymous-type properties can't host StringSyntax attributes, so a
            // read of one carries no usable metadata. Return null so GetSyntax
            // maps it to Unknown and SSA002/003 don't fire on values read from
            // `.Select(_ => new { _.Tagged })` projections.
            IPropertyReferenceOperation prop when prop.Property.ContainingType?.IsAnonymousType == true => null,
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

    static SyntaxInfo GetSyntax(ISymbol? symbol, SyntaxTypes types, bool conventionsEnabled)
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
                if (comment.IndexOf('|') >= 0)
                {
                    return SyntaxInfo.PresentUnion(
                        [..comment.Split('|', StringSplitOptions.RemoveEmptyEntries)]);
                }

                return SyntaxInfo.Present(comment);
            }

            if (conventionsEnabled && NameConventions.TryMatch(local.Name, out var conventionValue))
            {
                return SyntaxInfo.Present(conventionValue);
            }

            // `out var x`, `is string s`, `foreach (var x in ...)` and other
            // pattern/designation locals aren't declared via a LocalDeclarationStatement,
            // so there's no place for the codefix to attach `// language=<name>`. Treat
            // them as Unknown rather than NotPresent — same reasoning as invocation
            // results: the source isn't attributable, so SSA002 would be an unfixable
            // warning. Only keep NotPresent for locals that can actually host the fix.
            if (!CanHostLanguageComment(local))
            {
                return SyntaxInfo.Unknown;
            }

            return SyntaxInfo.NotPresent;
        }

        var info = GetSyntaxFromAttributes(symbol.GetAttributes(), types);
        if (info.State == SyntaxState.Present)
        {
            return info;
        }

        // `[return: Json]` (and other shortcut attributes) lives on the method's return
        // value, not the method symbol — `IMethodSymbol.GetAttributes()` won't surface it.
        // Check `GetReturnTypeAttributes()` as a fallback so `[return: Json]` is treated
        // the same as `[ReturnSyntax(Syntax.Json)]`.
        if (symbol is IMethodSymbol method)
        {
            var returnInfo = GetSyntaxFromAttributes(method.GetReturnTypeAttributes(), types);
            if (returnInfo.State == SyntaxState.Present)
            {
                return returnInfo;
            }
        }

        // Records: a primary-constructor parameter with [StringSyntax] doesn't
        // propagate the attribute to the synthesized property (the default
        // attribute target for such parameters is the parameter itself). Treat
        // the parameter's attribute as also applying to the generated property.
        if (symbol is IPropertySymbol property &&
            FindPrimaryConstructorParameter(property) is { } parameter)
        {
            var paramInfo = GetSyntaxFromAttributes(parameter.GetAttributes(), types);
            if (paramInfo.State == SyntaxState.Present)
            {
                return paramInfo;
            }
            info = paramInfo;
        }

        return ApplyConvention(info, symbol, conventionsEnabled);
    }

    // Promote NotPresent → Present(value) when the symbol's name matches a known
    // convention and the consumer has opted in. Convention-promotion overrides
    // the KnownUnannotatedAssemblies short-circuit further down — a Present
    // state never trips that path.
    static SyntaxInfo ApplyConvention(SyntaxInfo info, ISymbol symbol, bool conventionsEnabled)
    {
        if (!conventionsEnabled || info.State != SyntaxState.NotPresent)
        {
            return info;
        }

        if (symbol is IMethodSymbol)
        {
            return info;
        }

        if (NameConventions.TryMatch(symbol.Name, out var value))
        {
            return SyntaxInfo.Present(value);
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

    static bool CanHostLanguageComment(ILocalSymbol local)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax().FirstAncestorOrSelf<LocalDeclarationStatementSyntax>() is not null)
            {
                return true;
            }
        }

        return false;
    }

    static IOperation UnwrapConversions(IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }

    const string unionSyntaxAttributeName = "UnionSyntaxAttribute";
    const string returnSyntaxAttributeName = "ReturnSyntaxAttribute";
    const string shortcutAttributeNamespace = "StringSyntaxAttributeAnalyzer";

    // Names of shortcut-per-constant attributes emitted by SyntaxConstantsGenerator when
    // `StringSyntaxAnalyzer_EmitShortcutAttributes=true`. E.g. `[Html]` is recognized as
    // `[StringSyntax("Html")]`. Kept in sync with the generator's `shortcutNames` list.
    static readonly ImmutableHashSet<string> shortcutAttributeNames =
    [
        "CompositeFormat",
        "DateOnlyFormat",
        "DateTimeFormat",
        "EnumFormat",
        "GuidFormat",
        "Json",
        "NumericFormat",
        "Regex",
        "TimeOnlyFormat",
        "TimeSpanFormat",
        "Uri",
        "Xml",
        "Html",
        "Text",
        "Email",
        "Markdown",
        "Yaml",
        "Csv",
        "Sql"
    ];

    readonly struct SyntaxTypes(INamedTypeSymbol stringSyntax)
    {
        public INamedTypeSymbol StringSyntax { get; } = stringSyntax;
    }

    static bool IsAttributeNamed(AttributeData attribute, string typeName)
    {
        var type = attribute.AttributeClass;
        if (type is null)
        {
            return false;
        }

        return type.Name == typeName && IsInShortcutNamespace(type);
    }

    static bool IsInShortcutNamespace(INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.Name != shortcutAttributeNamespace)
        {
            return false;
        }

        return ns.ContainingNamespace?.IsGlobalNamespace ?? false;
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

            if (IsAttributeNamed(attribute, unionSyntaxAttributeName))
            {
                var values = ExtractUnionOptions(attribute);
                return SyntaxInfo.PresentUnion(values);
            }

            if (IsAttributeNamed(attribute, returnSyntaxAttributeName))
            {
                var values = ExtractUnionOptions(attribute);
                if (values.Length == 1)
                {
                    return SyntaxInfo.Present(values[0]);
                }
                return SyntaxInfo.PresentUnion(values);
            }

            if (TryMatchShortcutAttribute(attribute, out var shortcutValue))
            {
                return SyntaxInfo.Present(shortcutValue);
            }
        }

        return SyntaxInfo.NotPresent;
    }

    // Recognize `[Html]`, `[Json]`, ... emitted by SyntaxConstantsGenerator when the
    // consumer opts in with `StringSyntaxAnalyzer_EmitShortcutAttributes=true`. The
    // attribute is generated per-assembly (internal), so we match by fully-qualified
    // name rather than symbol identity — same as UnionSyntax/ReturnSyntax.
    static bool TryMatchShortcutAttribute(AttributeData attribute, out string value)
    {
        value = "";
        var type = attribute.AttributeClass;
        if (type is null)
        {
            return false;
        }

        if (!IsInShortcutNamespace(type))
        {
            return false;
        }

        var name = type.Name;
        if (!name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            return false;
        }

        var baseName = name.Substring(0, name.Length - "Attribute".Length);
        if (!shortcutAttributeNames.Contains(baseName))
        {
            return false;
        }

        value = baseName;
        return true;
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
        NamespaceSuppression suppression,
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
                    DescribeSymbol(targetSymbol),
                    SyntaxValueMatcher.FormatValues(target.Values)));
            }

            return;
        }

        if (source.State == SyntaxState.NotPresent &&
            target.State == SyntaxState.Present)
        {
            // Source in a suppressed namespace (BCL etc.) can't be annotated by the
            // consumer — skip rather than surfacing an unfixable SSA002. Mirrors the
            // target-side check on the SSA003 branch below. Same reasoning for
            // assemblies the generator confirmed have zero [StringSyntax] anywhere
            // (third-party libs that haven't adopted the attribute).
            if (suppression.Matches(sourceSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(sourceSymbol))
            {
                return;
            }

            // A source whose name already implies the target's syntax (e.g. parameter
            // `html` flowing into `[StringSyntax("Html")]`) is self-documenting — the
            // analyzer suppresses the diagnostic regardless of whether the consumer
            // opted in to `name_conventions`, since asking them to add the attribute
            // would just produce an annotation SSA008 would then flag as redundant.
            if (NameConventionMatches(sourceSymbol, target.Values))
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
                    DescribeSymbol(targetSymbol),
                    target));
            return;
        }

        if (source.State == SyntaxState.Present &&
            target.State == SyntaxState.NotPresent)
        {
            // SSA003: the target can't be fixed if it's in a namespace the user can't
            // edit (BCL by default), or in an assembly the generator confirmed has
            // no [StringSyntax] annotations at all.
            if (suppression.Matches(targetSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(targetSymbol))
            {
                return;
            }

            // Target whose name matches the source's syntax convention (e.g. source
            // `[Html]` → parameter named `html`) is treated as self-annotating. See
            // NameConventionMatches; same rationale as the SSA002 branch above.
            if (NameConventionMatches(targetSymbol, source.Values))
            {
                return;
            }

            // Fix site is the target symbol's declaration (add StringSyntax matching source).
            context.ReportDiagnostic(
                CreateFixableDiagnostic(
                    droppedFormatRule,
                    location,
                    targetSymbol,
                    DescribeSymbol(targetSymbol),
                    source));
        }
    }

    // Suppresses SSA002/SSA003/SSA005 when the unattributed side's symbol name
    // matches a known convention whose value aligns with the attributed side.
    // Intentionally runs regardless of the `name_conventions` opt-in: this is
    // "don't warn when the name speaks for itself", not "promote to Present".
    // The broader promote-on-convention behaviour that affects SSA001/SSA008
    // still needs the opt-in — see NameConventionsOption.
    static bool NameConventionMatches(ISymbol? symbol, ImmutableArray<string> attributedValues)
    {
        if (symbol is null ||
            attributedValues.IsDefaultOrEmpty)
        {
            return false;
        }

        var name = symbol.Name;

        // Exact name ↔ value match (case-insensitive): a field `ModifiedBy` flowing
        // into `[StringSyntax("ModifiedBy")]` is self-documenting and never needs
        // the attribute. Runs regardless of the `name_conventions` opt-in for the
        // same reason as the convention branch below.
        foreach (var value in attributedValues)
        {
            if (string.Equals(name, value, StringComparison.OrdinalIgnoreCase))
            {
                return true;
            }
        }

        if (!NameConventions.TryMatch(name, out var conventionValue))
        {
            return false;
        }

        foreach (var value in attributedValues)
        {
            if (SyntaxValueMatcher.SingleValuesMatch(conventionValue, value))
            {
                return true;
            }
        }

        return false;
    }

    // Human-readable "<kind> 'Type.Name'" label used in diagnostic messages so the
    // user sees which declaration is involved rather than a generic "a target".
    // Parameters also include their enclosing method since `name` alone doesn't
    // disambiguate across overloads.
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

    // Assignment/argument-style diagnostics (SSA002/SSA003) thread the opposite
    // side's description into {1} so the user sees which declaration lacks the
    // attribute. SSA002's format string is {source-desc, target-value}; SSA003
    // is {source-value, target-desc}. Callers pick the matching format.
    static Diagnostic CreateFixableDiagnostic(
        DiagnosticDescriptor rule,
        Location location,
        ISymbol? fixTarget,
        string oppositeDescription,
        SyntaxInfo info) =>
        Diagnostic.Create(
            rule,
            location,
            additionalLocations: GetAdditionalLocations(fixTarget),
            // Pipe-delimited so a UnionSyntax source can drive multiple codefix options
            // (one per value + one combined). The pipe is the same separator used in the
            // user-visible message — safe because values are identifier-like.
            properties: ImmutableDictionary<string, string?>.Empty.Add(valueKey, SyntaxValueMatcher.FormatValues(info.Values)),
            messageArgs: rule.Id == "SSA002"
                ? [oppositeDescription, SyntaxValueMatcher.FormatValues(info.Values)]
                : [SyntaxValueMatcher.FormatValues(info.Values), oppositeDescription]);

    static Location[]? GetAdditionalLocations(ISymbol? fixTarget)
    {
        var declaration = fixTarget?.DeclaringSyntaxReferences.FirstOrDefault();
        if (declaration is null)
        {
            return null;
        }

        return [Location.Create(declaration.SyntaxTree, declaration.Span)];
    }

    // Per-compilation bag for foreach loop-variable → element syntax bindings.
    // Locals don't support attributes in C#, so the only way a syntax value flows
    // through a foreach is by looking the loop local up here at use-sites.
    //
    // KvpBindings handles the key/value position split for foreach over a
    // key-value stream (Dictionary, IGrouping, IEnumerable<KeyValuePair<,>>,
    // etc). A single loop-variable can be in at most one of the two maps.
    sealed class LinqFlow
    {
        public ConcurrentDictionary<ILocalSymbol, ImmutableArray<string>> LocalBindings { get; } =
            new(SymbolEqualityComparer.Default);

        public ConcurrentDictionary<ILocalSymbol, KvpBinding> KvpBindings { get; } =
            new(SymbolEqualityComparer.Default);
    }

    enum KvpPosition { Key, Value }

    // A tagged element-position binding for KV-shaped streams. At most one of
    // Key/Value is Present: StringSyntax attributes don't carry a position
    // themselves, so ClassifyKvpPosition infers which side the tag applies to
    // from the symbol's type arguments.
    sealed class KvpBinding(SyntaxInfo key, SyntaxInfo value)
    {
        public SyntaxInfo Value { get; } = value;

        public SyntaxInfo Pick(KvpPosition position) =>
            position == KvpPosition.Key ? key : Value;
    }

    // Position-assignment rule:
    //   * Exactly one of K / V is string → tag applies to that side.
    //   * Both K and V are string → Value (empirically dominant; Key-side
    //     tagging on this shape is explicitly unsupported).
    //   * Neither is string → no position can legitimately hold a StringSyntax
    //     value; classification returns null and the attribute is silently
    //     ignored on this declaration.
    static KvpPosition? ClassifyKvpPosition(ITypeSymbol? symbolType)
    {
        if (!symbolType.TryGetKvpTypeArgs(out var key, out var value))
        {
            return null;
        }

        var keyIsString = key.SpecialType == SpecialType.System_String;
        var valueIsString = value.SpecialType == SpecialType.System_String;

        if (valueIsString)
        {
            return KvpPosition.Value;
        }

        if (keyIsString)
        {
            return KvpPosition.Key;
        }

        return null;
    }

    // Reads attributes off a KV-shaped symbol and lifts them into a KvpBinding
    // positioned by ClassifyKvpPosition. Returns null when the symbol's type
    // isn't KV-shaped, no position is tag-eligible, or no attribute resolves.
    static KvpBinding? GetKvpBinding(ISymbol symbol, SyntaxTypes types)
    {
        var position = ClassifyKvpPosition(symbol.GetDeclaredType());
        if (position is null)
        {
            return null;
        }

        var info = GetExplicitCollectionTags(symbol, types);
        if (info.State != SyntaxState.Present)
        {
            return null;
        }

        if (position == KvpPosition.Key)
        {
            return new(info, SyntaxInfo.NotPresent);
        }

        return new(SyntaxInfo.NotPresent, info);
    }

    // Walks element-preserving LINQ calls back through a KV-shaped stream to
    // the tagged source symbol. Select/SelectMany (reshaping) and Dictionary-
    // specific entry points (.Keys/.Values — handled separately in the single-T
    // path) break out of the walk. Returns null if the chain doesn't bottom out
    // at a KV-tagged symbol.
    static KvpBinding? GetReceiverKvpTags(IOperation receiver, SyntaxTypes types, LinqFlow linqFlow)
    {
        while (true)
        {
            receiver = receiver.Unwrap();

            if (receiver is IInvocationOperation inv &&
                IsElementPreserving(inv.TargetMethod))
            {
                var next = GetLinqReceiver(inv);
                if (next is null)
                {
                    return null;
                }

                receiver = next;
                continue;
            }

            if (receiver is ILocalReferenceOperation localRef &&
                linqFlow.KvpBindings.TryGetValue(localRef.Local, out var bound))
            {
                return bound;
            }

            var symbol = receiver.GetReferencedSymbol();
            if (symbol is null)
            {
                return null;
            }

            return GetKvpBinding(symbol, types);
        }
    }

    // Resolves the KvpBinding for the expression that PRODUCED a KeyValuePair
    // (or IGrouping) — used when we see a `.Key` / `.Value` (or `grouping.Key`)
    // access and need to find where the enclosing KV came from. Handles three
    // shapes: foreach-bound local, element-returning LINQ on a KV stream
    // (`dict.First()` et al), and a direct reference to a KV-typed symbol.
    static KvpBinding? GetKvpBindingFromOperation(
        IOperation operation,
        SyntaxTypes types,
        LinqFlow linqFlow)
    {
        operation = operation.Unwrap();

        if (operation is ILocalReferenceOperation localRef &&
            linqFlow.KvpBindings.TryGetValue(localRef.Local, out var bound))
        {
            return bound;
        }

        if (operation is IInvocationOperation inv &&
            inv.TargetMethod.IsLinqMethod() &&
            IsElementReturningLinq(inv.TargetMethod.Name))
        {
            var receiver = GetLinqReceiver(inv);
            if (receiver is not null)
            {
                return GetReceiverKvpTags(receiver, types, linqFlow);
            }
        }

        var symbol = operation.GetReferencedSymbol();
        if (symbol is null)
        {
            return null;
        }

        return GetKvpBinding(symbol, types);
    }

    // dict[k] read. Fires when the indexer is declared on a KV-shaped type and
    // its return type matches that type's Value-position. Value-side tag is
    // picked because an indexer returning V *is* a Value read by definition.
    // Kept narrow: doesn't claim indexers on non-dict-shaped types.
    static bool TryResolveDictionaryIndexer(
        IPropertyReferenceOperation indexerRef,
        SyntaxTypes types,
        LinqFlow linqFlow,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;

        var containing = indexerRef.Property.ContainingType;
        if (containing is null)
        {
            return false;
        }

        if (!containing.TryGetKvpTypeArgs(out _, out var value))
        {
            return false;
        }

        // Indexer return type must be V — filters out multi-indexer types that
        // also happen to be KV-shaped.
        if (!SymbolEqualityComparer.Default.Equals(indexerRef.Property.Type, value))
        {
            return false;
        }

        var binding = GetKvpBindingFromOperation(indexerRef.Instance!, types, linqFlow);
        if (binding is null ||
            binding.Value.State != SyntaxState.Present)
        {
            return false;
        }

        info = binding.Value;
        return true;
    }

    // dict.Keys / dict.Values as element-flow sources. `.Keys.First()` and
    // `.Values.First()` are common patterns; this hook intercepts the property
    // access in GetReceiverElementTags so the element syntax picked is the
    // matching side of the dict's KvpBinding (rather than NotPresent from the
    // BCL-declared Keys / Values property, which has no attributes).
    //
    // Returns true with `info` filled when the receiver is a tagged .Keys /
    // .Values; returns false to let the caller fall through to the standard
    // element-flow path.
    static bool TryResolveKvpCollectionView(
        IOperation receiver,
        SyntaxTypes types,
        LinqFlow linqFlow,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;

        if (receiver is not IPropertyReferenceOperation
            {
                Instance: { } instance,
                Property: { Name: "Keys" or "Values", ContainingType: { } propOwner } prop
            })
        {
            return false;
        }

        if (!propOwner.TryGetKvpTypeArgs(out _, out _))
        {
            return false;
        }

        var binding = GetKvpBindingFromOperation(instance, types, linqFlow);
        if (binding is null)
        {
            return false;
        }

        var side = prop.Name == "Keys" ? KvpPosition.Key : KvpPosition.Value;
        var picked = binding.Pick(side);
        if (picked.State != SyntaxState.Present)
        {
            return false;
        }

        info = picked;
        return true;
    }

    // Recognises .Key / .Value on a KeyValuePair<K,V> and .Key on an
    // IGrouping<K,T>. Callers pair this with GetKvpBindingFromOperation to
    // resolve the tag for the specific position being read.
    static bool IsKvpOrGroupingMember(ISymbol member, out KvpPosition side)
    {
        side = default;
        if (member.ContainingType is not { } containing)
        {
            return false;
        }

        if (Extensions.IsSystemCollectionsGenericKvp(containing))
        {
            if (member.Name == "Key")
            {
                side = KvpPosition.Key;
                return true;
            }

            if (member.Name == "Value")
            {
                side = KvpPosition.Value;
                return true;
            }
        }

        if (Extensions.IsSystemLinqIGrouping(containing) && member.Name == "Key")
        {
            side = KvpPosition.Key;
            return true;
        }

        return false;
    }

    // Resolves the source-side (read) operation's SyntaxInfo. First tries LINQ /
    // foreach element-flow paths (lambda parameter in a LINQ-shape extension,
    // element-returning LINQ invocation, foreach-bound local, array indexer), and
    // falls back to the scalar path (symbol + attributes + convention) otherwise.
    // The scalar path also has collection-tag suppression applied so passing a
    // tagged collection into a bare receiver slot doesn't spuriously fire SSA003.
    static (ISymbol? symbol, SyntaxInfo info) GetSourceInfo(
        IOperation operation,
        SyntaxTypes types,
        LinqFlow linqFlow,
        bool conventionsEnabled)
    {
        var unwrapped = operation.Unwrap();

        if (unwrapped is ILocalReferenceOperation localRef &&
            linqFlow.LocalBindings.TryGetValue(localRef.Local, out var boundValues))
        {
            return (localRef.Local, SyntaxInfo.PresentUnion(boundValues));
        }

        // kv.Key / kv.Value on a KeyValuePair, or grouping.Key on IGrouping —
        // resolve via the KV binding of whatever produced the enclosing KV
        // (foreach-bound local, element-returning LINQ, direct KV-typed ref).
        if (unwrapped is IPropertyReferenceOperation
            {
                Instance: not null
            } kvpProp &&
            IsKvpOrGroupingMember(kvpProp.Property, out var kvpSide))
        {
            var kvpBinding = GetKvpBindingFromOperation(kvpProp.Instance, types, linqFlow);
            if (kvpBinding is not null)
            {
                var picked = kvpBinding.Pick(kvpSide);
                if (picked.State == SyntaxState.Present)
                {
                    return (kvpProp.Property, picked);
                }
            }
        }

        // dict[k] indexer — returns the Value side of the enclosing KV shape.
        if (unwrapped is IPropertyReferenceOperation
            {
                Property.IsIndexer: true,
                Instance: not null
            } indexerRef &&
            TryResolveDictionaryIndexer(indexerRef, types, linqFlow, out var indexerInfo))
        {
            return (indexerRef.Property, indexerInfo);
        }

        if (unwrapped is IInvocationOperation inv &&
            TryResolveLinqElementReturn(inv, types, linqFlow, out var linqInfo))
        {
            return (inv.TargetMethod, linqInfo);
        }

        // Anon-type property read — honour `//language=X` written on the
        // originating member initializer (e.g. `new { /*language=Json*/ _.Data }`).
        // Only fires when the originating `new { … }` is reachable via unwrap +
        // local-init + element-returning LINQ / Select tracing.
        if (unwrapped is IPropertyReferenceOperation
            {
                Property.ContainingType.IsAnonymousType: true,
                Instance: { } anonInstance
            } anonReadRef &&
            TryResolveAnonymousMemberLanguageComment(anonReadRef, anonInstance, out var anonReadInfo))
        {
            return (anonReadRef.Property, anonReadInfo);
        }

        // Locals inherit the tag of their initializer expression when the
        // initializer resolves to a Present (e.g. `var x = await q.Select(_ => _.Tagged)
        // .SingleAsync()`). A language-injection comment still wins via GetSyntax;
        // this path only kicks in when the local itself has no comment but its
        // initializer carries a discoverable tag.
        if (unwrapped is ILocalReferenceOperation localRefInit &&
            TryResolveLocalInitializer(localRefInit, types, linqFlow, conventionsEnabled, out var initInfo))
        {
            return (localRefInit.Local, initInfo);
        }

        if (unwrapped is IParameterReferenceOperation param &&
            TryResolveLambdaParameterFromLinq(param, types, linqFlow, out var lambdaInfo))
        {
            return (param.Parameter, lambdaInfo);
        }

        if (unwrapped is IArrayElementReferenceOperation arrayElement)
        {
            var arrayInfo = GetReceiverElementTags(arrayElement.ArrayReference, types, linqFlow);
            if (arrayInfo.State == SyntaxState.Present)
            {
                return (null, arrayInfo);
            }
        }

        // Scalar path: existing symbol → attributes resolution, plus collection-tag
        // suppression for members typed as single-T enumerables.
        var symbol = GetSymbol(operation);
        var info = GetSyntax(symbol, types, conventionsEnabled);
        info = SuppressCollectionTag(GetDeclaredType(symbol), info);
        return (symbol, info);
    }

    static ITypeSymbol? GetDeclaredType(ISymbol? symbol) => symbol?.GetDeclaredType();

    // If the symbol's declared type is a single-T enumerable, any StringSyntax on
    // it is semantically an element-tag — not meaningful in scalar contexts. Drop
    // to Unknown so SSA001/SSA003 don't fire when a tagged collection is passed
    // into an untagged collection receiver slot (e.g. a LINQ-shape extension's
    // `this IEnumerable<T>` parameter). Element-tag consumers (foreach / .First() /
    // lambda params) bypass this via GetReceiverElementTags.
    static SyntaxInfo SuppressCollectionTag(ITypeSymbol? type, SyntaxInfo info)
    {
        if (info.State != SyntaxState.Present)
        {
            return info;
        }

        if (type.TryGetEnumerableElementType() is not null)
        {
            return SyntaxInfo.Unknown;
        }

        return info;
    }

    // `foreach (var x in collection)` binds `x` to the collection's element
    // syntax. Nested foreach over a tagged source works too because the receiver
    // resolution recurses through element-preserving calls.
    //
    // For KV-shaped streams (Dictionary, IGrouping, IEnumerable<KVP>) the
    // loop variable is a KeyValuePair; the tag lives on one position (Key or
    // Value, per ClassifyKvpPosition) and is recovered when `.Key` / `.Value`
    // is read inside the loop body.
    static void AnalyzeLoop(OperationAnalysisContext context, SyntaxTypes types, LinqFlow linqFlow)
    {
        if (context.Operation is not IForEachLoopOperation forEach)
        {
            return;
        }

        var loopVar = ExtractLoopLocal(forEach.LoopControlVariable);
        if (loopVar is null)
        {
            return;
        }

        var info = GetReceiverElementTags(forEach.Collection, types, linqFlow);
        if (info.State == SyntaxState.Present)
        {
            linqFlow.LocalBindings.TryAdd(loopVar, info.Values);
            return;
        }

        var kvp = GetReceiverKvpTags(forEach.Collection, types, linqFlow);
        if (kvp is not null)
        {
            linqFlow.KvpBindings.TryAdd(loopVar, kvp);
        }
    }

    static ILocalSymbol? ExtractLoopLocal(IOperation? controlVariable) =>
        controlVariable switch
        {
            IVariableDeclaratorOperation decl => decl.Symbol,
            ILocalReferenceOperation localRef => localRef.Local,
            _ => null
        };

    // If `param` is a single-parameter LINQ-style lambda (e.g. `Where`, `Select`,
    // `Any` body, or any extension method on IEnumerable<T> accepting a Func<T,..>),
    // and the enclosing invocation's receiver is a collection carrying an element
    // syntax, bind the lambda parameter to that element syntax. This is what lets
    // `docs.Any(d => d == literal)` resolve `d` without requiring an attribute on
    // the lambda parameter — attributes aren't even legal inside expression trees
    // (CS8972), so inference is the only way IQueryable predicates work.
    //
    // The gate is shape-based rather than name-based: any extension whose first
    // parameter is IEnumerable<T> / array participates, so third-party helpers
    // (MoreLINQ, EF .Include, custom paging) flow syntax the same way built-in
    // LINQ does. Element-returning calls (First/Single/...) are kept on a closed
    // allowlist because their semantic is specific; see TryResolveLinqElementReturn.
    static bool TryResolveLambdaParameterFromLinq(
        IParameterReferenceOperation param,
        SyntaxTypes types,
        LinqFlow linqFlow,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;

        if (param.Parameter.ContainingSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.LambdaMethod
            } lambdaMethod)
        {
            return false;
        }

        // Only bind the lambda's first parameter — TSource in every IEnumerable<T>
        // extension shape. Index overloads (Select/Where with int) take TSource as
        // parameter 0. Multi-source shapes like Zip / SelectMany with an
        // intermediate collection aren't handled in this pass.
        if (param.Parameter.Ordinal != 0 ||
            lambdaMethod.Parameters.Length is 0 or > 2)
        {
            return false;
        }

        var anonymous = FindEnclosingAnonymousFunction(param);
        if (anonymous is null)
        {
            return false;
        }

        var invocation = FindEnclosingLinqInvocation(anonymous);
        if (invocation is null)
        {
            return false;
        }

        if (!IsEnumerableShapeExtension(invocation.TargetMethod))
        {
            return false;
        }

        var receiver = GetLinqReceiver(invocation);
        if (receiver is null)
        {
            return false;
        }

        var element = receiver.Type.TryGetEnumerableElementType();
        if (element is null ||
            !SymbolEqualityComparer.Default.Equals(element, param.Parameter.Type))
        {
            return false;
        }

        var elementInfo = GetReceiverElementTags(receiver, types, linqFlow);
        if (elementInfo.State != SyntaxState.Present)
        {
            return false;
        }

        info = elementInfo;
        return true;
    }

    // Parses a `//language=X` (optionally pipe-delimited union) value into a
    // SyntaxInfo for use as either a target tag on an anon-member write or a
    // propagated source tag on an anon-member read.
    static SyntaxInfo BuildCommentInfo(string value)
    {
        if (value.IndexOf('|') < 0)
        {
            return SyntaxInfo.Present(value);
        }

        var parts = value.Split('|', StringSplitOptions.RemoveEmptyEntries);
        return SyntaxInfo.PresentUnion([..parts]);
    }

    // For a read of `anon.Prop`, find the originating `new { … }` that produced
    // the anon instance, then check whether the initializer for `Prop` carries a
    // `//language=X` comment. The instance trace walks await/conversion wrappers,
    // unwraps locals to their initializer, peels element-returning LINQ
    // (`.First/.Single/.FirstAsync/…`) and unwraps a `.Select(_ => new { … })`
    // projection to reach the anon creation.
    static bool TryResolveAnonymousMemberLanguageComment(
        IPropertyReferenceOperation anonProp,
        IOperation instance,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;
        var creation = FindOriginatingAnonymousCreation(instance);
        if (creation is null)
        {
            return false;
        }

        var name = anonProp.Property.Name;
        foreach (var initializer in creation.Initializers)
        {
            if (initializer is not ISimpleAssignmentOperation
                {
                    Target: IPropertyReferenceOperation targetProp
                } assignment ||
                !string.Equals(targetProp.Property.Name, name, StringComparison.Ordinal))
            {
                continue;
            }

            if (!LanguageCommentReader.TryReadFromNode(assignment.Syntax, out var value))
            {
                return false;
            }

            info = BuildCommentInfo(value);
            return true;
        }

        return false;
    }

    // Walks instance → anon creation through: direct creation, local-init
    // chasing, element-returning LINQ/async, element-preserving LINQ, and
    // .Select projecting to an anon creation. Returns null if the origin can't
    // be pinned down syntactically (method return, parameter, etc.).
    static IAnonymousObjectCreationOperation? FindOriginatingAnonymousCreation(IOperation operation)
    {
        var visited = 0;
        while (visited++ < 32)
        {
            operation = operation.Unwrap();

            if (operation is IAnonymousObjectCreationOperation anon)
            {
                return anon;
            }

            if (operation is ILocalReferenceOperation localRef)
            {
                var reference = localRef.Local.DeclaringSyntaxReferences.FirstOrDefault();
                if (reference?.GetSyntax() is not VariableDeclaratorSyntax
                    {
                        Initializer.Value: { } initializerSyntax
                    } ||
                    localRef.SemanticModel?.GetOperation(initializerSyntax) is not { } initOp)
                {
                    return null;
                }

                operation = initOp;
                continue;
            }

            if (operation is IInvocationOperation invocation)
            {
                var methodName = invocation.TargetMethod.Name;
                var baseName = methodName.Length > 5 &&
                               methodName.EndsWith("Async", StringComparison.Ordinal)
                    ? methodName.Substring(0, methodName.Length - 5)
                    : methodName;

                if (IsElementReturningLinq(baseName) || IsElementPreservingLinq(baseName))
                {
                    var receiver = GetLinqReceiver(invocation);
                    if (receiver is null)
                    {
                        return null;
                    }

                    operation = receiver;
                    continue;
                }

                if (IsSelectCall(invocation.TargetMethod))
                {
                    var selector = FindSelectorArgument(invocation);
                    if (selector is null)
                    {
                        return null;
                    }

                    selector = selector.Unwrap();
                    var lambda = selector switch
                    {
                        IDelegateCreationOperation creation => creation.Target.Unwrap() as IAnonymousFunctionOperation,
                        IAnonymousFunctionOperation anonFunc => anonFunc,
                        _ => null
                    };

                    if (lambda is null)
                    {
                        return null;
                    }

                    var body = GetSingleReturnExpression(lambda);
                    if (body is null)
                    {
                        return null;
                    }

                    operation = body;
                    continue;
                }
            }

            return null;
        }

        return null;
    }

    // `var x = <expr>;` — trace <expr>'s SyntaxInfo and bind it to `x`. Only
    // Present results propagate; NotPresent/Unknown fall through so existing
    // local-level logic (language-injection comments, SSA002 codefix) still
    // drives. Language-injection on the local takes precedence — this helper
    // only fills the gap when the author has not commented the local.
    static bool TryResolveLocalInitializer(
        ILocalReferenceOperation localRef,
        SyntaxTypes types,
        LinqFlow linqFlow,
        bool conventionsEnabled,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;

        if (LanguageCommentReader.TryRead(localRef.Local, out _))
        {
            return false;
        }

        var reference = localRef.Local.DeclaringSyntaxReferences.FirstOrDefault();
        if (reference is null)
        {
            return false;
        }

        if (reference.GetSyntax() is not VariableDeclaratorSyntax
            {
                Initializer.Value: { } initializerSyntax
            })
        {
            return false;
        }

        var semanticModel = localRef.SemanticModel;
        if (semanticModel is null)
        {
            return false;
        }

        var initializerOp = semanticModel.GetOperation(initializerSyntax);
        if (initializerOp is null)
        {
            return false;
        }

        var (_, resolved) = GetSourceInfo(initializerOp, types, linqFlow, conventionsEnabled);
        if (resolved.State != SyntaxState.Present)
        {
            return false;
        }

        info = resolved;
        return true;
    }

    // For element-returning LINQ (`.First()`, `.Single()`, `.ElementAt()` etc.)
    // surface the receiver's element syntax as the invocation's result syntax —
    // so `jsonDocs.First()` is treated as a single Json-tagged string. Async
    // variants (EF Core `FirstAsync`, `SingleAsync`, …) participate by shape:
    // an element-returning name + "Async" returning Task<T> / ValueTask<T>
    // where T matches the receiver's element type.
    static bool TryResolveLinqElementReturn(
        IInvocationOperation invocation,
        SyntaxTypes types,
        LinqFlow linqFlow,
        out SyntaxInfo info)
    {
        info = SyntaxInfo.Unknown;

        var targetMethod = invocation.TargetMethod;
        var name = targetMethod.Name;
        var isAsync = false;

        if (targetMethod.IsLinqMethod() && IsElementReturningLinq(name))
        {
            // sync System.Linq variant
        }
        else if (name.Length > 5 &&
                 name.EndsWith("Async", StringComparison.Ordinal) &&
                 IsElementReturningLinq(name.Substring(0, name.Length - 5)))
        {
            isAsync = true;
        }
        else
        {
            return false;
        }

        var receiver = GetLinqReceiver(invocation);
        if (receiver is null)
        {
            return false;
        }

        var resultType = invocation.Type;
        if (isAsync)
        {
            if (resultType is INamedTypeSymbol { IsGenericType: true, TypeArguments.Length: 1 } task &&
                task.Name is "Task" or "ValueTask")
            {
                resultType = task.TypeArguments[0];
            }
            else
            {
                return false;
            }
        }

        var element = receiver.Type.TryGetEnumerableElementType();
        if (element is null ||
            !SymbolEqualityComparer.Default.Equals(element, resultType))
        {
            return false;
        }

        var elementInfo = GetReceiverElementTags(receiver, types, linqFlow);
        if (elementInfo.State != SyntaxState.Present)
        {
            return false;
        }

        info = elementInfo;
        return true;
    }

    // Walk element-preserving calls backwards until we hit an expression whose
    // symbol (property/field/parameter/return) carries explicit StringSyntax /
    // UnionSyntax / ReturnSyntax attributes on a collection-typed declaration.
    // Convention tagging and shortcut attributes participate via the shared
    // attribute lookup, but the receiver must be explicitly tagged — generic
    // collections without any attribute never propagate.
    //
    // Select / SelectMany get their own handling (GetSelectElementTags) because
    // the result element type can differ from the source — the selector decides
    // the new syntax.
    static SyntaxInfo GetReceiverElementTags(IOperation receiver, SyntaxTypes types, LinqFlow linqFlow)
    {
        // Iterates element-preserving LINQ chains instead of recursing — long
        // method chains (common in query-heavy code) would otherwise grow the
        // stack linearly with chain length.
        while (true)
        {
            receiver = receiver.Unwrap();

            if (TryResolveKvpCollectionView(receiver, types, linqFlow, out var kvpView))
            {
                return kvpView;
            }

            if (receiver is IInvocationOperation inv)
            {
                var targetMethod = inv.TargetMethod;

                if (IsSelectCall(targetMethod))
                {
                    return GetSelectElementTags(inv, types, linqFlow);
                }

                if (IsElementPreserving(targetMethod))
                {
                    var next = GetLinqReceiver(inv);
                    if (next is null)
                    {
                        return SyntaxInfo.Unknown;
                    }

                    receiver = next;
                    continue;
                }
            }

            var symbol = receiver.GetReferencedSymbol();
            if (symbol is null)
            {
                return SyntaxInfo.Unknown;
            }

            var symbolType = symbol.GetDeclaredType();

            // KV-shaped sources route via position classification. An attribute
            // on a Dictionary / IEnumerable<KVP> / IGrouping tags ONE position;
            // a scalar element tag is only appropriate when the enumeration
            // actually yields that position:
            //   * Dictionary / IEnumerable<KVP>: element is KVP — never a
            //     scalar tag. Return Unknown here; kv.Key / kv.Value / dict[k]
            //     / dict.Keys / dict.Values carry the tag instead.
            //   * IGrouping<K, T> with Value-position rule: elements are T, so
            //     the single-T tag flows to them.
            //   * IGrouping<K, T> with Key-position rule: elements are T, not
            //     tagged; the tag only surfaces on `grouping.Key`.
            if (symbolType.TryGetKvpTypeArgs(out _, out _))
            {
                var position = ClassifyKvpPosition(symbolType);
                if (position is null)
                {
                    return SyntaxInfo.Unknown;
                }

                var element = symbolType.TryGetEnumerableElementType();
                if (element is not null && element.TryGetKvpTypeArgs(out _, out _))
                {
                    // Elements are themselves KV-shaped (KVP or IGrouping). A
                    // scalar element tag would lie about the element's shape;
                    // the tag surfaces through .Key / .Value / .Keys / .Values
                    // reads on the individual elements instead.
                    return SyntaxInfo.Unknown;
                }

                if (position == KvpPosition.Value)
                {
                    return GetExplicitCollectionTags(symbol, types);
                }

                return SyntaxInfo.Unknown;
            }

            if (symbolType.TryGetEnumerableElementType() is null)
            {
                return SyntaxInfo.Unknown;
            }

            return GetExplicitCollectionTags(symbol, types);
        }
    }

    // Resolve StringSyntax/UnionSyntax/ReturnSyntax (and shortcut attributes) for
    // a collection-typed symbol, walking the override and interface-implementation
    // chain so an interface-declared tag flows through its implementations. Name-
    // convention tagging is deliberately skipped — a collection-typed member whose
    // name happens to match the convention would spuriously acquire a tag no
    // caller can opt out of.
    static SyntaxInfo GetExplicitCollectionTags(ISymbol symbol, SyntaxTypes types)
    {
        var direct = GetSyntaxFromAttributes(symbol.GetAttributes(), types);
        if (direct.State == SyntaxState.Present)
        {
            return direct;
        }

        if (symbol is IMethodSymbol method)
        {
            var returnInfo = GetMethodSyntax(method, types);
            if (returnInfo.State == SyntaxState.Present)
            {
                return returnInfo;
            }

            var overridden = method.OverriddenMethod;
            while (overridden is not null)
            {
                var info = GetMethodSyntax(overridden, types);
                if (info.State == SyntaxState.Present)
                {
                    return info;
                }

                overridden = overridden.OverriddenMethod;
            }

            foreach (var iface in method.ContainingType?.AllInterfaces ?? [])
            {
                foreach (var ifaceMember in iface.GetMembers(method.Name).OfType<IMethodSymbol>())
                {
                    var impl = method.ContainingType!.FindImplementationForInterfaceMember(ifaceMember);
                    if (!SymbolEqualityComparer.Default.Equals(impl, method))
                    {
                        continue;
                    }

                    var info = GetMethodSyntax(ifaceMember, types);
                    if (info.State == SyntaxState.Present)
                    {
                        return info;
                    }
                }
            }

            return SyntaxInfo.NotPresent;
        }

        if (symbol is IPropertySymbol property)
        {
            var inherited = GetPropertyFromHierarchy(property, types);
            if (inherited.State == SyntaxState.Present)
            {
                return inherited;
            }

            // Record primary-ctor parameters carry their [StringSyntax] on the
            // parameter itself, not the synthesized property. Surface them here so
            // element-flow through a record's collection property works.
            if (FindPrimaryConstructorParameter(property) is { } recordParam)
            {
                var fromParam = GetSyntaxFromAttributes(recordParam.GetAttributes(), types);
                if (fromParam.State == SyntaxState.Present)
                {
                    return fromParam;
                }
            }
        }

        return SyntaxInfo.NotPresent;
    }

    // [ReturnSyntax] lives on the method symbol itself; [return: StringSyntax] /
    // [return: Html] live on the method's return-value attribute set. Callers that
    // want the method's effective return-syntax need to check both.
    static SyntaxInfo GetMethodSyntax(IMethodSymbol method, SyntaxTypes types)
    {
        var direct = GetSyntaxFromAttributes(method.GetAttributes(), types);
        if (direct.State == SyntaxState.Present)
        {
            return direct;
        }

        return GetSyntaxFromAttributes(method.GetReturnTypeAttributes(), types);
    }

    static SyntaxInfo GetPropertyFromHierarchy(IPropertySymbol property, SyntaxTypes types)
    {
        var overridden = property.OverriddenProperty;
        while (overridden is not null)
        {
            var info = GetSyntaxFromAttributes(overridden.GetAttributes(), types);
            if (info.State == SyntaxState.Present)
            {
                return info;
            }

            overridden = overridden.OverriddenProperty;
        }

        foreach (var ifaceMember in property.ExplicitInterfaceImplementations)
        {
            var info = GetSyntaxFromAttributes(ifaceMember.GetAttributes(), types);
            if (info.State == SyntaxState.Present)
            {
                return info;
            }
        }

        var containingType = property.ContainingType;
        if (containingType is null)
        {
            return SyntaxInfo.NotPresent;
        }

        foreach (var iface in containingType.AllInterfaces)
        {
            foreach (var ifaceMember in iface.GetMembers(property.Name).OfType<IPropertySymbol>())
            {
                var impl = containingType.FindImplementationForInterfaceMember(ifaceMember);
                if (!SymbolEqualityComparer.Default.Equals(impl, property))
                {
                    continue;
                }

                var info = GetSyntaxFromAttributes(ifaceMember.GetAttributes(), types);
                if (info.State == SyntaxState.Present)
                {
                    return info;
                }
            }
        }

        return SyntaxInfo.NotPresent;
    }

    // Select/SelectMany can preserve, transform, or drop the syntax depending on
    // the selector. Three shapes are recognised, all statically inspectable:
    //   1. Identity lambda `x => x` — result element syntax = receiver's.
    //   2. Method group `.Select(SomeMethod)` — result = method's [return: ...].
    //   3. Expression-bodied lambda whose body resolves to a known syntax.
    // Other selector shapes (multi-statement lambdas, untagged expressions) drop.
    static SyntaxInfo GetSelectElementTags(IInvocationOperation invocation, SyntaxTypes types, LinqFlow linqFlow)
    {
        var selector = FindSelectorArgument(invocation);
        if (selector is null)
        {
            return SyntaxInfo.Unknown;
        }

        selector = selector.Unwrap();

        // IEnumerable<T>.Select passes a Func<T,R> — the lambda sits inside an
        // IDelegateCreationOperation. IQueryable<T>.Select passes an
        // Expression<Func<T,R>> — the lambda surfaces directly after unwrap.
        // Peel either wrapper so method-group and expression-tree selectors flow
        // through the same analysis.
        var target = selector switch
        {
            IDelegateCreationOperation creation => creation.Target.Unwrap(),
            IAnonymousFunctionOperation or IMethodReferenceOperation => selector,
            _ => (IOperation?)null
        };

        if (target is IMethodReferenceOperation methodRef)
        {
            // [ReturnSyntax] is applied to the method symbol; [return: StringSyntax]
            // / [return: Html] live on the return-value attribute set. Check both so
            // either form drives the element syntax out of the Select.
            var methodInfo = GetSyntaxFromAttributes(methodRef.Method.GetAttributes(), types);
            if (methodInfo.State == SyntaxState.Present)
            {
                return methodInfo;
            }

            var returnInfo = GetSyntaxFromAttributes(
                methodRef.Method.GetReturnTypeAttributes(),
                types);
            return returnInfo.State == SyntaxState.Present
                ? returnInfo
                : SyntaxInfo.Unknown;
        }

        if (target is IAnonymousFunctionOperation lambda)
        {
            var body = GetSingleReturnExpression(lambda);
            if (body is null)
            {
                return SyntaxInfo.Unknown;
            }

            if (IsIdentityReference(body, lambda))
            {
                var next = GetLinqReceiver(invocation);
                if (next is null)
                {
                    return SyntaxInfo.Unknown;
                }

                return GetReceiverElementTags(next, types, linqFlow);
            }

            // Fall back to a scalar-source resolution of the body — a tagged
            // invocation or property access inside the lambda body becomes the
            // new element syntax.
            var (_, info) = GetSourceInfo(body, types, linqFlow, conventionsEnabled: false);
            return info.State == SyntaxState.Present ? info : SyntaxInfo.Unknown;
        }

        return SyntaxInfo.Unknown;
    }

    // The selector sits after the source in Enumerable/Queryable.Select; for
    // extension calls the source is Arguments[0] and the selector Arguments[1].
    // For instance-form Select (custom providers), Instance is the source and
    // Arguments[0] is the selector.
    static IOperation? FindSelectorArgument(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Arguments.Length > 0 ? invocation.Arguments[0].Value : null;
        }

        if (invocation.TargetMethod.IsExtensionMethod &&
            invocation.Arguments.Length > 1)
        {
            return invocation.Arguments[1].Value;
        }

        return null;
    }

    // Lambda bodies surface as a synthesised block with a single return — both
    // for expression-bodied and brace-bodied single-return lambdas. Anything
    // with more than one statement is treated as opaque (the last statement
    // isn't reliably the result).
    static IOperation? GetSingleReturnExpression(IAnonymousFunctionOperation lambda)
    {
        var block = lambda.Body;
        if (block.Operations.Length != 1)
        {
            return null;
        }

        if (block.Operations[0] is IReturnOperation { ReturnedValue: { } value })
        {
            return value.Unwrap();
        }

        return null;
    }

    static bool IsIdentityReference(IOperation body, IAnonymousFunctionOperation lambda)
    {
        if (body is not IParameterReferenceOperation paramRef)
        {
            return false;
        }

        var parameters = lambda.Symbol.Parameters;
        return parameters.Length > 0 &&
               SymbolEqualityComparer.Default.Equals(paramRef.Parameter, parameters[0]);
    }

    // Element preservation is accepted via two channels: the named-LINQ list
    // (closed, covers every System.Linq.Enumerable/Queryable method whose
    // signature matches IEnumerable<T> → IEnumerable<T>), and a shape-based rule
    // that lets third-party extensions with the same signature participate —
    // MoreLINQ, EF `.Include`, custom paging helpers. The shape rule requires
    // the method to be an extension on IEnumerable<T> whose return is also
    // IEnumerable<T> with the same element T.
    //
    // Comparison runs on OriginalDefinition so that generic methods declared as
    // `IEnumerable<T> Foo<T>(IEnumerable<T>)` match — without OriginalDefinition
    // the input type parameter and return type parameter are distinct symbols
    // after construction, which would defeat the check.
    static bool IsElementPreserving(IMethodSymbol method)
    {
        if (method.IsLinqMethod() && IsElementPreservingLinq(method.Name))
        {
            return true;
        }

        if (!method.IsExtensionMethod)
        {
            return false;
        }

        var definition = (method.ReducedFrom ?? method).OriginalDefinition;
        if (definition.Parameters.Length == 0)
        {
            return false;
        }

        var inputElement = definition.Parameters[0].Type.TryGetEnumerableElementType();
        var outputElement = definition.ReturnType.TryGetEnumerableElementType();
        return inputElement is not null &&
               outputElement is not null &&
               SymbolEqualityComparer.Default.Equals(inputElement, outputElement);
    }

    static bool IsSelectCall(IMethodSymbol method) =>
        method.IsLinqMethod() &&
        method.Name is "Select" or "SelectMany";

    // An extension method whose receiver carries a discoverable element type.
    // This is the gate for LINQ-shape recognition — it lets `static T[] Custom<T>
    // (this IEnumerable<T> src, Func<T,bool> f)` flow syntax without hard-coding
    // the method name.
    static bool IsEnumerableShapeExtension(IMethodSymbol method) =>
        GetExtensionReceiverType(method) is { } receiverType &&
        receiverType.TryGetEnumerableElementType() is not null;

    // For a reduced extension-method call (`x.Ext(...)`), `method.Parameters`
    // excludes the receiver — the "this" parameter only appears on the unreduced
    // symbol, which ReducedFrom surfaces. For calls written in static form
    // (`Ext(x, ...)`) the method is already unreduced, so ReducedFrom is null and
    // Parameters[0] is the receiver.
    static ITypeSymbol? GetExtensionReceiverType(IMethodSymbol method)
    {
        if (!method.IsExtensionMethod)
        {
            return null;
        }

        var full = method.ReducedFrom ?? method;
        if (full.Parameters.Length == 0)
        {
            return null;
        }

        return full.Parameters[0].Type;
    }

    static IOperation? FindEnclosingAnonymousFunction(IOperation operation)
    {
        var current = operation.Parent;
        while (current is not null)
        {
            if (current is IAnonymousFunctionOperation)
            {
                return current;
            }

            current = current.Parent;
        }

        return null;
    }

    static IInvocationOperation? FindEnclosingLinqInvocation(IOperation lambda)
    {
        var current = lambda.Parent;
        while (current is not null)
        {
            if (current is IInvocationOperation invocation)
            {
                return invocation;
            }

            // Walk through delegate creation, conversion, argument wrappers that
            // the compiler threads between the lambda and the invocation. An
            // unrelated enclosing operation (e.g. a different invocation body)
            // means the lambda isn't a direct argument of the LINQ call we care
            // about.
            if (current is IDelegateCreationOperation or IConversionOperation or IArgumentOperation)
            {
                current = current.Parent;
                continue;
            }

            return null;
        }

        return null;
    }

    // Extension-method invocations of LINQ put the receiver in Arguments[0] and
    // leave Instance null. Instance-method LINQ (rare but e.g. Queryable instance
    // forms on custom providers) uses Instance. Handle both so both shapes
    // propagate.
    static IOperation? GetLinqReceiver(IInvocationOperation invocation)
    {
        if (invocation.Instance is not null)
        {
            return invocation.Instance;
        }

        if (invocation.TargetMethod.IsExtensionMethod &&
            invocation.Arguments.Length > 0)
        {
            return invocation.Arguments[0].Value;
        }

        return null;
    }

    static bool IsElementReturningLinq(string methodName) =>
        methodName is
            "First" or "FirstOrDefault" or
            "Single" or "SingleOrDefault" or
            "Last" or "LastOrDefault" or
            "ElementAt" or "ElementAtOrDefault" or
            "Min" or "Max" or
            "Aggregate";

    static bool IsElementPreservingLinq(string methodName) =>
        methodName is
            "Where" or
            "OrderBy" or "OrderByDescending" or
            "ThenBy" or "ThenByDescending" or
            "Reverse" or
            "Take" or "TakeWhile" or "TakeLast" or
            "Skip" or "SkipWhile" or "SkipLast" or
            "Distinct" or "DistinctBy" or
            "Concat" or "Union" or "UnionBy" or
            "Intersect" or "IntersectBy" or
            "Except" or "ExceptBy" or
            "AsEnumerable" or "AsQueryable" or
            "ToArray" or "ToList" or "ToHashSet" or
            "Append" or "Prepend";
}

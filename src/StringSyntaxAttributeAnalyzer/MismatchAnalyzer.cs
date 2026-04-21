using System.Diagnostics.CodeAnalysis;

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

            start.RegisterOperationAction(
                _ => AnalyzeArgument(_, types, suppression, conventions),
                OperationKind.Argument);
            start.RegisterOperationAction(
                _ => AnalyzeSimpleAssignment(_, types, suppression, conventions),
                OperationKind.SimpleAssignment);
            start.RegisterOperationAction(
                _ => AnalyzePropertyInitializer(_, types, suppression, conventions),
                OperationKind.PropertyInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeFieldInitializer(_, types, suppression, conventions),
                OperationKind.FieldInitializer);
            start.RegisterOperationAction(
                _ => AnalyzeBinaryOperator(_, types, suppression, conventions),
                OperationKind.BinaryOperator);
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
        NameConventionsOption conventions)
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
        var sourceSymbol = GetSymbol(argument.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types, conventionsEnabled);
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
        NameConventionsOption conventions)
    {
        var assignment = (ISimpleAssignmentOperation)context.Operation;
        var targetSymbol = GetSymbol(assignment.Target);
        if (targetSymbol is null)
        {
            return;
        }

        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var targetInfo = GetSyntax(targetSymbol, types, conventionsEnabled);
        var sourceSymbol = GetSymbol(assignment.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types, conventionsEnabled);
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
        NameConventionsOption conventions)
    {
        var init = (IPropertyInitializerOperation)context.Operation;
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types, conventionsEnabled);
        var patterns = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
        foreach (var property in init.InitializedProperties)
        {
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
        NameConventionsOption conventions)
    {
        var init = (IFieldInitializerOperation)context.Operation;
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);
        var sourceSymbol = GetSymbol(init.Value);
        var sourceInfo = GetSyntax(sourceSymbol, types, conventionsEnabled);
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
        NamespaceSuppression suppression,
        NameConventionsOption conventions)
    {
        var binary = (IBinaryOperation)context.Operation;
        if (binary.OperatorKind is not (BinaryOperatorKind.Equals or BinaryOperatorKind.NotEquals))
        {
            return;
        }

        var suppressedNamespaces = suppression.GetPatterns(context.Operation.Syntax.SyntaxTree);
        var conventionsEnabled = conventions.IsEnabled(context.Operation.Syntax.SyntaxTree);

        var leftSymbol = GetSymbol(binary.LeftOperand);
        var rightSymbol = GetSymbol(binary.RightOperand);
        var leftInfo = GetSyntax(leftSymbol, types, conventionsEnabled);
        var rightInfo = GetSyntax(rightSymbol, types, conventionsEnabled);

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
                suppression.Matches(rightSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(rightSymbol))
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
                suppression.Matches(leftSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(leftSymbol))
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
            // edit (BCL by default), or in an assembly the generator confirmed has
            // no [StringSyntax] annotations at all.
            if (suppression.Matches(targetSymbol, suppressedNamespaces) ||
                KnownUnannotatedAssemblies.Contains(targetSymbol))
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
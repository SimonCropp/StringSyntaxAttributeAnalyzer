static class Extensions
{

    // Peels off conversions, `await`, and `??` so the resolver sees the value-producing
    // operation underneath. An `await task` result carries the syntax of the method
    // that produced the task, so unwrapping lets `[return: StringSyntax]` on an async
    // method flow through the await. For `x ?? y`, the LHS is the tag-bearing side
    // (RHS is typically an untagged fallback like `""`).
    public static IOperation Unwrap(this IOperation operation)
    {
        while (true)
        {
            switch (operation)
            {
                case IConversionOperation conversion:
                    operation = conversion.Operand;
                    continue;
                case IAwaitOperation await:
                    operation = await.Operation;
                    continue;
                case ICoalesceOperation coalesce:
                    operation = coalesce.Value;
                    continue;
                default:
                    return operation;
            }
        }
    }

    // Resolves the underlying declaration symbol for an expression. Returns null for
    // expression shapes that have no single declaration — literals, compound
    // expressions, etc.
    public static ISymbol? GetReferencedSymbol(this IOperation operation) =>
        operation.Unwrap() switch
        {
            IPropertyReferenceOperation prop => prop.Property,
            IFieldReferenceOperation field => field.Field.ResolveBackingField(),
            IParameterReferenceOperation param => param.Parameter.ResolveAccessorValue(),
            IInvocationOperation invocation => invocation.TargetMethod,
            _ => null
        };

    // The implicit `value` of a set or init accessor *is* the property's value: assigning
    // to `[StringSyntax("Json")] string Payload` means what is assigned is Json. Read as a
    // bare parameter it carries no attributes and has no DeclaringSyntaxReferences, so the
    // most ordinary shape there is — an annotated property over an annotated backing field
    // — reported SSA002 against itself, with no fix site to attach anything to.
    //
    // Resolving to the property fixes both halves at once: the annotation becomes readable,
    // and when the property has none the diagnostic lands on a declaration the codefix can
    // actually write to. Applied on the element-flow path too, so `foreach (var x in value)`
    // in a collection property's setter binds `x` to the property's element tags.
    public static ISymbol ResolveAccessorValue(this IParameterSymbol parameter)
    {
        // An indexer's set accessor takes its index parameters *before* `value`, so only
        // the last one is the assigned value — binding `index` to the indexer would claim
        // the index carries the indexer's syntax.
        if (parameter.ContainingSymbol is IMethodSymbol
            {
                MethodKind: MethodKind.PropertySet,
                AssociatedSymbol: IPropertySymbol property
            } setter &&
            parameter.Ordinal == setter.Parameters.Length - 1)
        {
            return property;
        }

        return parameter;
    }

    // C# 14's `field` keyword reads and writes a property's synthesized backing field. That
    // field is the property's storage, not a declaration of its own: it carries none of the
    // property's attributes and has no syntax an attribute could be added to. Read as
    // itself, `field = value` in the setter of an annotated property reported SSA003
    // against the property's own storage, and a `field == value` change guard SSA005, each
    // with a fix that could not be applied.
    //
    // Resolved to the property, the same way a setter's implicit `value` is, both sides of
    // those name the same symbol and agree.
    public static ISymbol ResolveBackingField(this IFieldSymbol field)
    {
        if (field.AssociatedSymbol is IPropertySymbol property)
        {
            return property;
        }

        return field;
    }

    // Returns the declared type of a value-producing symbol: property / field /
    // parameter / local → its Type; method → ReturnType. Other symbol kinds don't
    // have a single "declared value type" and return null.
    public static ITypeSymbol? GetDeclaredType(this ISymbol symbol) =>
        symbol switch
        {
            IPropertySymbol p => p.Type,
            IFieldSymbol f => f.Type,
            IParameterSymbol pa => pa.Type,
            ILocalSymbol l => l.Type,
            IMethodSymbol m => m.ReturnType,
            _ => null
        };

    // Arrays are IEnumerable<T>. Otherwise a type must implement exactly one
    // IEnumerable<T> construction for the caller to pick a single element type.
    // Dictionary<K,V> implements IEnumerable<KeyValuePair<K,V>> — single element
    // type, but composite — LINQ-flow consumers further gate on the lambda's param
    // type to avoid composite matches.
    public static ITypeSymbol? TryGetEnumerableElementType(this ITypeSymbol? type)
    {
        if (type is null)
        {
            return null;
        }

        if (type is IArrayTypeSymbol array)
        {
            return array.ElementType;
        }

        if (type is not INamedTypeSymbol named)
        {
            return null;
        }

        // string implements IEnumerable<char>, but is the canonical scalar for
        // StringSyntax — exclude it so LINQ-flow doesn't treat `char` as an element.
        if (named.SpecialType == SpecialType.System_String)
        {
            return null;
        }

        if (named is
            {
                IsGenericType: true,
                ConstructedFrom.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T
            })
        {
            return named.TypeArguments[0];
        }

        ITypeSymbol? found = null;
        foreach (var iface in named.AllInterfaces)
        {
            if (iface is
                {
                    IsGenericType: true,
                    ConstructedFrom.SpecialType: SpecialType.System_Collections_Generic_IEnumerable_T
                })
            {
                if (found is not null &&
                    !SymbolEqualityComparer.Default.Equals(found, iface.TypeArguments[0]))
                {
                    return null;
                }

                found = iface.TypeArguments[0];
            }
        }

        return found;
    }

    // Matches System.Collections.Generic.KeyValuePair<K, V> by name + namespace
    // chain, avoiding a ToDisplayString allocation and working across assembly
    // boundaries.
    public static bool IsSystemCollectionsGenericKvp(INamedTypeSymbol type) =>
        type is
        {
            Name: "KeyValuePair",
            Arity: 2,
            ContainingNamespace:
            {
                Name: "Generic",
                ContainingNamespace:
                {
                    Name: "Collections",
                    ContainingNamespace.Name: "System"
                }
            }
        };

    public static bool IsSystemLinqIGrouping(INamedTypeSymbol type) =>
        type is
        {
            Name: "IGrouping",
            Arity: 2,
            ContainingNamespace:
            {
                Name: "Linq",
                ContainingNamespace.Name: "System"
            }
        };

    // Recognises a type that carries "key" and "value" positions — used to
    // decide which position a StringSyntax attribute applies to. Covers
    // KeyValuePair<K,V>, IGrouping<K,T>, and any IEnumerable<KeyValuePair<K,V>>
    // (Dictionary, IDictionary, IReadOnlyDictionary, ILookup-emitted sequences,
    // query results shaped like `.Select((k, v) => new KVP(k, v))`, etc).
    public static bool TryGetKvpTypeArgs(
        this ITypeSymbol? type,
        [NotNullWhen(true)] out ITypeSymbol? key,
        [NotNullWhen(true)] out ITypeSymbol? value)
    {
        key = null;
        value = null;
        if (type is not INamedTypeSymbol named)
        {
            return false;
        }

        if (IsSystemCollectionsGenericKvp(named))
        {
            key = named.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }

        if (IsSystemLinqIGrouping(named))
        {
            key = named.TypeArguments[0];
            value = named.TypeArguments[1];
            return true;
        }

        foreach (var @interface in named.AllInterfaces)
        {
            if (IsSystemLinqIGrouping(@interface))
            {
                key = @interface.TypeArguments[0];
                value = @interface.TypeArguments[1];
                return true;
            }
        }

        // Dictionary / IDictionary / IReadOnlyDictionary / IEnumerable<KVP>,
        // plus IEnumerable<IGrouping<K,T>> (GroupBy results, ILookup emits,
        // caller-supplied projections). Peel exactly one enumerable layer —
        // nested enumerables cannot become KV-shaped, and recursing unbounded
        // stack-overflows on self-referential types like `class Node :
        // IEnumerable<Node>`.
        if (type.TryGetEnumerableElementType() is not INamedTypeSymbol element)
        {
            return false;
        }

        if (IsSystemCollectionsGenericKvp(element) || IsSystemLinqIGrouping(element))
        {
            key = element.TypeArguments[0];
            value = element.TypeArguments[1];
            return true;
        }

        foreach (var @interface in element.AllInterfaces)
        {
            if (IsSystemLinqIGrouping(@interface))
            {
                key = @interface.TypeArguments[0];
                value = @interface.TypeArguments[1];
                return true;
            }
        }

        return false;
    }

    public static bool IsTaggableScalar(this ITypeSymbol returnType)
    {
        if (returnType.SpecialType == SpecialType.System_String)
        {
            return true;
        }

        return returnType is INamedTypeSymbol
        {
            IsGenericType: true,
            TypeArguments: [{ SpecialType: SpecialType.System_String }],
            Name: "Task" or "ValueTask"
        };
    }

    // A "value slot" is a typed-object or generic-T target where strings flow as plain
    // values (logging, collections, generic extension methods). Passing a StringSyntax-
    // attributed value into such a slot doesn't meaningfully "drop" the format — the
    // receiver was never going to honour it. Skip SSA001/003/004/005 in those cases.
    public static bool IsGenericValueSlot(this ITypeSymbol? type) =>
        type is not null &&
        (type.SpecialType == SpecialType.System_Object ||
         type.TypeKind == TypeKind.TypeParameter ||
         (type is IArrayTypeSymbol array && array.ElementType.IsGenericValueSlot()));

    // The source-side counterpart of IsGenericValueSlot: true when the symbol's *declared*
    // type is a type parameter, or a Task/ValueTask of one.
    //
    // Such a member has nowhere to put an annotation that means anything. [StringSyntax]
    // on `Box<T>.Value` claims every Box<T> holds that syntax rather than the Box<string>
    // at the call site, and [ReturnSyntax] on `LoadAsync<T>()` the same — so reporting
    // SSA002 here offers a fix that changes the wrong thing. A bare-`T` return was already
    // refused in GetSymbol; this covers the property, field, parameter and Task<T> shapes
    // that went with it.
    //
    // Locals are deliberately absent: their `//language=` comment sits on one declaration
    // in one body, so it never spans substitutions the way an attribute on a member does.
    public static bool IsGenericValueSource(this ISymbol symbol)
    {
        // OriginalDefinition so a substituted `Box<string>.Value` still reads as `T`.
        var type = symbol.OriginalDefinition switch
        {
            IParameterSymbol parameter => parameter.Type,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            IMethodSymbol method => method.ReturnType,
            _ => null
        };

        if (type is null)
        {
            return false;
        }

        if (type.TypeKind == TypeKind.TypeParameter)
        {
            return true;
        }

        return type is INamedTypeSymbol
        {
            IsGenericType: true,
            TypeArguments: [{ TypeKind: TypeKind.TypeParameter }],
            Name: "Task" or "ValueTask"
        };
    }

    // Use OriginalDefinition so a generic method's `T value` parameter reads as TypeKind
    // TypeParameter even when the call site has substituted T=string.
    public static ITypeSymbol? GetTargetType(this ISymbol symbol) =>
        symbol.OriginalDefinition switch
        {
            IParameterSymbol parameter => parameter.Type,
            IPropertySymbol property => property.Type,
            IFieldSymbol field => field.Type,
            // Locals belong here for the same reason the others do. Omitting them meant
            // `object o = …; pattern == o` reported SSA005 while the identical comparison
            // against an `object` parameter was exempt — a string held as an object is not
            // carrying a syntax either way.
            ILocalSymbol local => local.Type,
            _ => null
        };

    public static IParameterSymbol? FindPrimaryConstructorParameter(this IPropertySymbol property)
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

    // The statement a local is actually declared by: declarator → declaration → statement,
    // matched exactly rather than walked up to.
    //
    // An ancestor walk finds *some* local declaration for almost any local. A `foreach`
    // variable or an `out var` designation sitting inside another local's initializer
    // would resolve to that outer statement — so the analyzer read the outer local's
    // `//language=` comment as its own, and the codefix offered to write one there, which
    // the value being reported never passes through. Locals with no declaration statement
    // of their own have nowhere to host a comment, and belong in Unknown.
    public static LocalDeclarationStatementSyntax? FindDeclarationStatement(this ILocalSymbol local)
    {
        foreach (var reference in local.DeclaringSyntaxReferences)
        {
            if (reference.GetSyntax() is VariableDeclaratorSyntax
                {
                    Parent: VariableDeclarationSyntax
                    {
                        Parent: LocalDeclarationStatementSyntax statement
                    }
                })
            {
                return statement;
            }
        }

        return null;
    }

    public static bool CanHostLanguageComment(this ILocalSymbol local) =>
        local.FindDeclarationStatement() is not null;

    public static IOperation UnwrapConversions(this IOperation operation)
    {
        while (operation is IConversionOperation conversion)
        {
            operation = conversion.Operand;
        }

        return operation;
    }
}

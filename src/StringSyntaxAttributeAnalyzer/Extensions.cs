using System.Diagnostics.CodeAnalysis;

static class Extensions
{
    // True when the method is declared on System.Linq.Enumerable or System.Linq.Queryable.
    // Matching by containing-type name and namespace chain avoids a string allocation
    // for ToDisplayString and works across assembly boundaries where identical type
    // definitions have distinct symbol identities.
    public static bool IsLinqMethod(this IMethodSymbol method)
    {
        var containing = method.ContainingType;
        if (containing is null)
        {
            return false;
        }

        var name = containing.Name;
        if (name != "Enumerable" &&
            name != "Queryable")
        {
            return false;
        }

        return containing.ContainingNamespace is
        {
            Name: "Linq",
            ContainingNamespace.Name: "System"
        };
    }

    // Peels off conversions and `await` so the resolver sees the value-producing
    // operation underneath. An `await task` result carries the syntax of the method
    // that produced the task, so unwrapping lets `[return: StringSyntax]` on an async
    // method flow through the await.
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
            IFieldReferenceOperation field => field.Field,
            IParameterReferenceOperation param => param.Parameter,
            IInvocationOperation invocation => invocation.TargetMethod,
            _ => null
        };

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
        // caller-supplied projections). The recursive call lets a single-T
        // enumerable of a KV-shaped element type adopt that element's K/V
        // positions — so `[StringSyntax]` on `IEnumerable<IGrouping<string, T>>`
        // picks up the same Key-position rule that applies to IGrouping itself.
        var element = type.TryGetEnumerableElementType();
        return element is not null &&
               element.TryGetKvpTypeArgs(out key, out value);
    }
}
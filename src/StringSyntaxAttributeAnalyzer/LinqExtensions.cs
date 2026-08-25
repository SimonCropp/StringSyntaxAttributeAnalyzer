// Shape recognition for LINQ-style call chains. The analyzer tracks element syntax
// through `Where`/`Select`/`First`/... and through third-party extensions with the
// same IEnumerable<T> signature, so these helpers answer "does this method preserve
// the element?" / "does it return one?" and locate the receiver, selector and lambda
// body of a call. Nothing here knows about StringSyntax — they only describe shape.
static class LinqExtensions
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
    public static bool IsElementPreserving(this IMethodSymbol method)
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

    public static bool IsSelectCall(this IMethodSymbol method) =>
        method.IsLinqMethod() &&
        method.Name is "Select" or "SelectMany";

    // An extension method whose receiver carries a discoverable element type.
    // This is the gate for LINQ-shape recognition — it lets `static T[] Custom<T>
    // (this IEnumerable<T> src, Func<T,bool> f)` flow syntax without hard-coding
    // the method name.
    public static bool IsEnumerableShapeExtension(this IMethodSymbol method) =>
        GetExtensionReceiverType(method) is { } receiverType &&
        receiverType.TryGetEnumerableElementType() is not null;

    // For a reduced extension-method call (`x.Ext(...)`), `method.Parameters`
    // excludes the receiver — the "this" parameter only appears on the unreduced
    // symbol, which ReducedFrom surfaces. For calls written in static form
    // (`Ext(x, ...)`) the method is already unreduced, so ReducedFrom is null and
    // Parameters[0] is the receiver.
    public static ITypeSymbol? GetExtensionReceiverType(this IMethodSymbol method)
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

    // Extension-method invocations of LINQ put the receiver in Arguments[0] and
    // leave Instance null. Instance-method LINQ (rare but e.g. Queryable instance
    // forms on custom providers) uses Instance. Handle both so both shapes
    // propagate.
    public static IOperation? GetLinqReceiver(this IInvocationOperation invocation)
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

    // The selector sits after the source in Enumerable/Queryable.Select; for
    // extension calls the source is Arguments[0] and the selector Arguments[1].
    // For instance-form Select (custom providers), Instance is the source and
    // Arguments[0] is the selector.
    public static IOperation? FindSelectorArgument(this IInvocationOperation invocation)
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

    public static IOperation? FindEnclosingAnonymousFunction(this IOperation operation)
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

    public static IInvocationOperation? FindEnclosingLinqInvocation(this IOperation lambda)
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

    // The collection a single-parameter LINQ-style lambda's element parameter is
    // bound to — the receiver of `Where`, `Select`, `Any`, or any extension method
    // on IEnumerable<T> accepting a Func<T,..>. `param` must be the lambda's first
    // parameter, TSource in every IEnumerable<T> extension shape. Index overloads
    // (Select/Where with int) take TSource as parameter 0. Multi-source shapes like
    // Zip / SelectMany with an intermediate collection aren't handled in this pass.
    //
    // The gate is shape-based rather than name-based: any extension whose first
    // parameter is IEnumerable<T> / array participates, so third-party helpers
    // (MoreLINQ, EF .Include, custom paging) flow syntax the same way built-in
    // LINQ does. Element-returning calls (First/Single/...) are kept on a closed
    // allowlist because their semantic is specific; see TryResolveLinqElementReturn.
    //
    // Both the tag path (a receiver carrying an element syntax) and the
    // anonymous-creation path (a receiver projected from `new { … }`) enter through
    // here, so a lambda parameter resolves against exactly one definition of what it
    // iterates.
    public static IOperation? GetLinqLambdaReceiver(this IParameterReferenceOperation param)
    {
        if (param.Parameter.ContainingSymbol is not IMethodSymbol
            {
                MethodKind: MethodKind.LambdaMethod
            } lambdaMethod)
        {
            return null;
        }

        if (param.Parameter.Ordinal != 0 ||
            lambdaMethod.Parameters.Length is 0 or > 2)
        {
            return null;
        }

        var anonymous = FindEnclosingAnonymousFunction(param);
        if (anonymous is null)
        {
            return null;
        }

        var invocation = FindEnclosingLinqInvocation(anonymous);
        if (invocation is null)
        {
            return null;
        }

        if (!IsEnumerableShapeExtension(invocation.TargetMethod))
        {
            return null;
        }

        var receiver = GetLinqReceiver(invocation);
        if (receiver is null)
        {
            return null;
        }

        var element = receiver.Type.TryGetEnumerableElementType();
        if (element is null ||
            !SymbolEqualityComparer.Default.Equals(element, param.Parameter.Type))
        {
            return null;
        }

        return receiver;
    }

    public static ILocalSymbol? ExtractLoopLocal(this IOperation? controlVariable) =>
        controlVariable switch
        {
            IVariableDeclaratorOperation decl => decl.Symbol,
            ILocalReferenceOperation localRef => localRef.Local,
            _ => null
        };

    // Lambda bodies surface as a synthesised block with a single return — both
    // for expression-bodied and brace-bodied single-return lambdas. Anything
    // with more than one statement is treated as opaque (the last statement
    // isn't reliably the result).
    public static IOperation? GetSingleReturnExpression(this IAnonymousFunctionOperation lambda)
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

    public static bool IsIdentityReference(this IOperation body, IAnonymousFunctionOperation lambda)
    {
        if (body is not IParameterReferenceOperation paramRef)
        {
            return false;
        }

        var parameters = lambda.Symbol.Parameters;
        return parameters.Length > 0 &&
               SymbolEqualityComparer.Default.Equals(paramRef.Parameter, parameters[0]);
    }

    public static bool IsElementReturningLinq(string methodName) =>
        methodName is
            "First" or "FirstOrDefault" or
            "Single" or "SingleOrDefault" or
            "Last" or "LastOrDefault" or
            "ElementAt" or "ElementAtOrDefault" or
            "Min" or "Max" or
            "Aggregate";

    public static bool IsElementPreservingLinq(string methodName) =>
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

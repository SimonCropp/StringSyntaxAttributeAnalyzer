// Lookup of well-known [StringSyntax(...)] annotations harvested from common
// assemblies via reflection by KnownStringSyntaxGenerationTests. Used as a fallback
// in MismatchAnalyzer when the live ISymbol carries no [StringSyntax] of its own —
// covers older targets (netstandard2.0, .NET Framework) and third-party libraries
// that haven't been annotated upstream.
//
// Keys are XML documentation comment IDs (the same format produced by
// ISymbol.GetDocumentationCommentId()) with a "#paramName" suffix for parameters
// and a "#return" suffix for method return values. The generator (reflection side)
// builds matching keys using ReflectionDocId — the two must stay in lock-step.
public static partial class KnownStringSyntax
{
    public static bool TryLookup(ISymbol symbol, out string value)
    {
        var key = KeyFor(symbol);
        if (key is not null && Lookup.TryGetValue(key, out var found))
        {
            value = found;
            return true;
        }

        value = "";
        return false;
    }

    static string? KeyFor(ISymbol symbol)
    {
        if (symbol is IParameterSymbol parameter)
        {
            var containerId = parameter.ContainingSymbol.GetDocumentationCommentId();
            return containerId is null ? null : $"{containerId}#{parameter.Name}";
        }

        if (symbol is IMethodSymbol method)
        {
            var id = method.GetDocumentationCommentId();
            return id is null ? null : $"{id}#return";
        }

        return symbol.GetDocumentationCommentId();
    }
}

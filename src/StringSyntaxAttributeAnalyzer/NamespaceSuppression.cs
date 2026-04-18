// EditorConfig knob + pattern matching for namespace-based suppression. Symbols
// whose containing type lives in a suppressed namespace are skipped on both the
// SSA003 target-side and the SSA002 source-side paths — by default the BCL
// (`System*,Microsoft*`), since its APIs can't be retroactively annotated.
// Patterns support a trailing `*` wildcard (prefix match). Example override:
//   stringsyntax.suppressed_target_namespaces = System*,Microsoft*,MyCompany.Legacy*
static class NamespaceSuppression
{
    const string optionsKey = "stringsyntax.suppressed_target_namespaces";
    const string defaultPatterns = "System*,Microsoft*";

    public static string[] ReadPatterns(AnalyzerOptions options)
    {
        var raw = options.AnalyzerConfigOptionsProvider.GlobalOptions
            .TryGetValue(optionsKey, out var configured)
            ? configured
            : defaultPatterns;

        return raw
            .Split(',')
            .Select(_ => _.Trim())
            .Where(_ => _.Length > 0)
            .ToArray();
    }

    public static bool Matches(ISymbol? symbol, string[] patterns)
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
}

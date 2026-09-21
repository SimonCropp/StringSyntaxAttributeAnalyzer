// Recognition and reading of the attribute family the analyzer understands:
// StringSyntax itself, the UnionSyntax/ReturnSyntax companions, and the per-constant
// shortcut attributes SyntaxConstantsGenerator emits. All of these are generated into
// the consumer's own compilation, so they are matched by name and namespace rather
// than symbol identity — identical declarations in different assemblies are distinct
// symbols.
static class SyntaxAttributeExtensions
{
    public const string UnionSyntaxAttributeName = "UnionSyntaxAttribute";
    public const string ReturnSyntaxAttributeName = "ReturnSyntaxAttribute";
    public const string ShortcutAttributeNamespace = "StringSyntaxAttributeAnalyzer";

    // `System.Diagnostics.CodeAnalysis`, innermost first — walked outwards so the match
    // costs no display-string allocation on a path that sees every attribute of every
    // symbol.
    static string[] stringSyntaxNamespace = ["CodeAnalysis", "Diagnostics", "System"];

    // StringSyntaxAttribute ships in the BCL only on net7+/netstandard2.1. Everywhere
    // else it is polyfilled as an internal per-assembly type — including by this
    // package's own generator, which emits one into every consumer that targets
    // netstandard2.0 or net4x. So a library's `[StringSyntax("Json")]` and the
    // consumer's own StringSyntaxAttribute are routinely *different symbols*, and
    // comparing against the compilation's copy by identity silently ignores every
    // annotation that crossed an assembly boundary. Match by name and namespace, the
    // same way UnionSyntax, ReturnSyntax and the shortcuts are matched.
    public static bool IsStringSyntax(this AttributeData attribute)
    {
        if (attribute.AttributeClass is not {Name: "StringSyntaxAttribute"} type)
        {
            return false;
        }

        var ns = type.ContainingNamespace;
        foreach (var part in stringSyntaxNamespace)
        {
            if (ns is null ||
                ns.Name != part)
            {
                return false;
            }

            ns = ns.ContainingNamespace;
        }

        return ns?.IsGlobalNamespace ?? false;
    }

    // Names of shortcut-per-constant attributes emitted by SyntaxConstantsGenerator when
    // `StringSyntaxAnalyzer_EmitShortcutAttributes=true`. E.g. `[Html]` is recognized as
    // `[StringSyntax("Html")]`. Kept in sync with the generator's `shortcutNames` list.
    public static readonly ImmutableHashSet<string> ShortcutAttributeNames =
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

    public static bool IsNamed(this AttributeData attribute, string typeName)
    {
        var type = attribute.AttributeClass;
        if (type is null)
        {
            return false;
        }

        return type.Name == typeName && type.IsInShortcutNamespace();
    }

    public static bool IsInShortcutNamespace(this INamedTypeSymbol type)
    {
        var ns = type.ContainingNamespace;
        if (ns is null || ns.Name != ShortcutAttributeNamespace)
        {
            return false;
        }

        return ns.ContainingNamespace?.IsGlobalNamespace ?? false;
    }

    // Recognize `[Html]`, `[Json]`, ... emitted by SyntaxConstantsGenerator when the
    // consumer opts in with `StringSyntaxAnalyzer_EmitShortcutAttributes=true`. The
    // attribute is generated per-assembly (internal), so we match by fully-qualified
    // name rather than symbol identity — same as UnionSyntax/ReturnSyntax.
    // Matches a shortcut attribute (e.g. `[Html]`) by simple name regardless of
    // namespace. The canonical shortcuts live in `StringSyntaxAttributeAnalyzer`
    // (emitted when `EmitShortcutAttributes=true`), but consumers sometimes hand-roll
    // an `HtmlAttribute` of their own — typically before discovering this analyzer,
    // or because a sibling library (e.g. Parchment) recognises `[Html]` by simple
    // name and they want the marker without opting into the source generator.
    // Recognising both keeps mismatch analysis (SSA001/SSA002 etc.) consistent
    // whichever flavour is used.
    public static bool TryMatchShortcutAttribute(this AttributeData attribute, out string value)
    {
        value = "";
        var type = attribute.AttributeClass;
        if (type is null)
        {
            return false;
        }

        var name = type.Name;
        if (!name.EndsWith("Attribute", StringComparison.Ordinal))
        {
            return false;
        }

        var baseName = name.Substring(0, name.Length - "Attribute".Length);
        if (!ShortcutAttributeNames.Contains(baseName))
        {
            return false;
        }

        value = baseName;
        return true;
    }

    // `StringSyntaxAttribute(string syntax, params object[] arguments)` lets an annotation
    // carry more than the syntax value — `[StringSyntax("Regex", RegexOptions.IgnorePattern
    // Whitespace)]` being the common one. Neither a shortcut attribute nor a name
    // convention can express those extra arguments, so an annotation carrying them is not
    // redundant however well the syntax value matches: SSA007 and SSA008 both offered to
    // replace or delete it, and both fixes dropped the options on the floor.
    public static bool CarriesExtraArguments(this AttributeData attribute)
    {
        if (attribute.ConstructorArguments.Length < 2)
        {
            return false;
        }

        var extra = attribute.ConstructorArguments[1];
        if (extra.Kind != TypedConstantKind.Array)
        {
            return true;
        }

        // A null or empty params array carries nothing, so there is nothing to lose by
        // replacing the annotation. IsNull is checked first for the same reason as in
        // ExtractUnionOptions: reading Values off a null array throws.
        return extra is {IsNull: false, Values.Length: > 0};
    }

    public static ImmutableArray<string> ExtractUnionOptions(this AttributeData attribute)
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

        // `params string[]` accepts null, so `[UnionSyntax(null)]` is legal C# and legal
        // metadata. For a null array TypedConstant.Values is a *default* ImmutableArray,
        // where even reading Length throws — which surfaced as AD0001 and took every flow
        // involving the symbol out of analysis.
        if (first.IsNull)
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

    public static string FoldShortcutKey(this string name)
    {
        if (name.Length == 0)
        {
            return name;
        }

        return char.ToLowerInvariant(name[0]) + name[1..];
    }
}

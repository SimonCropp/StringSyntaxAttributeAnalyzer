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

        return char.ToLowerInvariant(name[0]) + name.Substring(1);
    }
}

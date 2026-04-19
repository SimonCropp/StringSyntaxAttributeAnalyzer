// Names of the constants emitted on the `Syntax` class by SyntaxConstantsGenerator.
// Duplicated (rather than shared via ProjectReference) to keep the codefix project
// decoupled from the analyzer project — same reasoning as the duplicated diagnostic
// id strings. When a new constant is added to the generator, add it here too.
static class KnownSyntaxConstants
{
    // Exposed (internal) so tests can cross-check against the constants the
    // generator actually emits — catches the case where a new constant is added
    // to SyntaxConstantsGenerator but forgotten here, which would silently
    // degrade the codefix to emitting a string literal.
    internal static readonly ImmutableHashSet<string> Names =
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

    public static bool IsKnown(string value) => Names.Contains(value);
}

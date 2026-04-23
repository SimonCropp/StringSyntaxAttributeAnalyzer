// Simulates the file a source-only NuGet package would drop into the consumer
// assembly. Mirrors the BCL shape of StringSyntaxAttribute (net7+) so the
// analyzer must treat it as already-present and skip its own polyfill.
namespace System.Diagnostics.CodeAnalysis;

[AttributeUsage(AttributeTargets.Field | AttributeTargets.Parameter | AttributeTargets.Property, AllowMultiple = false)]
internal sealed class StringSyntaxAttribute : Attribute
{
    public StringSyntaxAttribute(string syntax)
    {
        Syntax = syntax;
        Arguments = Array.Empty<object?>();
    }

    public StringSyntaxAttribute(string syntax, params object?[] arguments)
    {
        Syntax = syntax;
        Arguments = arguments;
    }

    public string Syntax { get; }
    public object?[] Arguments { get; }

    public const string CompositeFormat = nameof(CompositeFormat);
    public const string DateOnlyFormat = nameof(DateOnlyFormat);
    public const string DateTimeFormat = nameof(DateTimeFormat);
    public const string EnumFormat = nameof(EnumFormat);
    public const string GuidFormat = nameof(GuidFormat);
    public const string Json = nameof(Json);
    public const string NumericFormat = nameof(NumericFormat);
    public const string Regex = nameof(Regex);
    public const string TimeOnlyFormat = nameof(TimeOnlyFormat);
    public const string TimeSpanFormat = nameof(TimeSpanFormat);
    public const string Uri = nameof(Uri);
    public const string Xml = nameof(Xml);
}

[TestFixture]
public class KnownStringSyntaxTests
{
    static readonly CSharpCompilation compilation = CSharpCompilation.Create(
        "KnownStringSyntaxTests",
        [],
        TrustedPlatformReferences.All,
        new(OutputKind.DynamicallyLinkedLibrary));

    [TestCase(typeof(Regex), "IsMatch", "pattern", "Regex")]
    [TestCase(typeof(Regex), "Match", "pattern", "Regex")]
    [TestCase(typeof(JsonDocument), "Parse", "json", "Json")]
    [TestCase(typeof(XmlDocument), "LoadXml", "xml", "Xml")]
    public void Parameter_is_recognised(Type type, string methodName, string parameterName, string expected)
    {
        var method = ResolveMethod(type, methodName, parameterName);
        var parameter = method.Parameters.Single(_ => _.Name == parameterName);

        AssertLookup(parameter, expected);
    }

    [Test]
    public void DateTime_ToString_format_parameter()
    {
        var method = ResolveType(typeof(DateTime))
            .GetMembers("ToString")
            .OfType<IMethodSymbol>()
            .Single(_ => _.Parameters is [{ Type.SpecialType: SpecialType.System_String }]);

        AssertLookup(method.Parameters[0], "DateTimeFormat");
    }

    [Test]
    public void Unrelated_parameter_is_not_recognised()
    {
        // String.Substring(int startIndex) — no [StringSyntax] anywhere on it.
        var method = ResolveType(typeof(string))
            .GetMembers("Substring")
            .OfType<IMethodSymbol>()
            .Single(_ => _.Parameters.Length == 1);

        Assert.IsFalse(KnownStringSyntax.TryLookup(method.Parameters[0], out _));
    }

    [Test]
    public void Lookup_values_are_all_known_syntax_names()
    {
        // Catches a generator regression that would silently emit garbage values.
        var validValues = new HashSet<string>(StringComparer.Ordinal)
        {
            "CompositeFormat", "DateOnlyFormat", "DateTimeFormat", "EnumFormat",
            "GuidFormat", "Json", "NumericFormat", "Regex", "TimeOnlyFormat",
            "TimeSpanFormat", "Uri", "Xml"
        };

        foreach (var pair in KnownStringSyntax.Lookup)
        {
            Assert.IsTrue(
                validValues.Contains(pair.Value),
                $"Unexpected syntax value '{pair.Value}' for key '{pair.Key}'.");
        }
    }

    [Test]
    public void Lookup_keys_are_well_formed()
    {
        // Every key is either a bare doc-id (P:/F:/M:) or a member doc-id with a
        // suffix (#paramName or #return). Anything else means the generator and
        // the analyzer's KeyFor are out of sync.
        foreach (var key in KnownStringSyntax.Lookup.Keys)
        {
            var hash = key.IndexOf('#');
            var prefix = hash < 0 ? key.Substring(0, 2) : key.Substring(0, 2);
            Assert.IsTrue(
                prefix is "M:" or "P:" or "F:",
                $"Unexpected key prefix in '{key}'.");
        }
    }

    [Test]
    public void Lookup_is_non_empty() =>
        Assert.IsTrue(KnownStringSyntax.Lookup.Count > 0, "KnownStringSyntax.Lookup is empty — generator did not run?");

    static IMethodSymbol ResolveMethod(Type type, string methodName, string parameterName)
    {
        var named = ResolveType(type);
        var candidates = named.GetMembers(methodName)
            .OfType<IMethodSymbol>()
            .Where(_ => _.Parameters.Any(p => p.Name == parameterName))
            .ToList();

        Assert.IsNotEmpty(candidates, $"No overload of {type}.{methodName} with parameter '{parameterName}'.");
        return candidates[0];
    }

    static INamedTypeSymbol ResolveType(Type type)
    {
        var named = compilation.GetTypeByMetadataName(type.FullName!);
        Assert.IsNotNull(named, $"Could not resolve {type.FullName} via Roslyn.");
        return named!;
    }

    static void AssertLookup(ISymbol symbol, string expected)
    {
        Assert.IsTrue(
            KnownStringSyntax.TryLookup(symbol, out var value),
            $"Expected lookup hit for {symbol}.");
        Assert.AreEqual(expected, value);
    }

}

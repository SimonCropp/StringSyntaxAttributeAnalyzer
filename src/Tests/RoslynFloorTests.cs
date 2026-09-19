// The Roslyn version an analyzer is compiled against is a shipping contract: any compiler
// older than it skips the assembly with CS9057. That is a warning, not an error, so the
// symptom is every diagnostic silently disappearing — and, because the source generator
// goes with it, a CS0246 cascade on [UnionSyntax] / [ReturnSyntax] in consumer code.
//
// Bumping Microsoft.CodeAnalysis.CSharp in Directory.Packages.props therefore drops every
// toolchain below the new version, which is a release note rather than a routine update.
// Pinning the version baked into the built assemblies means that cannot happen by
// accident: this fails, rather than consumers on an older SDK finding out.
public class RoslynFloorTests
{
    // Roslyn 4.11 is the .NET 8 SDK's compiler. It is also the practical lower bound —
    // 4.8 and below bundle a System.Collections.Immutable without CollectionBuilder
    // support, where this repo's collection expressions fail to compile (CS9210).
    const string floor = "4.11.0.0";

    [Test]
    public Task AnalyzerIsBuiltAgainstTheFloor() =>
        AssertRoslynReference(typeof(MismatchAnalyzer).Assembly);

    [Test]
    public Task CodeFixesAreBuiltAgainstTheFloor() =>
        AssertRoslynReference(typeof(AddStringSyntaxCodeFixProvider).Assembly);

    static async Task AssertRoslynReference(Assembly assembly)
    {
        // Every Microsoft.CodeAnalysis.* reference is checked, not just the core one: the
        // codefixes also bind Workspaces, and the compiler's load check looks at all of
        // them.
        var versions = assembly
            .GetReferencedAssemblies()
            .Where(_ => _.Name?.StartsWith("Microsoft.CodeAnalysis", StringComparison.Ordinal) == true)
            .Select(_ => $"{_.Name} {_.Version}")
            .Where(_ => !_.EndsWith(floor, StringComparison.Ordinal))
            .OrderBy(_ => _);

        await Assert.That(string.Join(", ", versions)).IsEqualTo("");
    }
}

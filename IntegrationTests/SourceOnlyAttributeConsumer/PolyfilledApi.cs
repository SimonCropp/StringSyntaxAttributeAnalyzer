// A public annotated API on an assembly that carries its own StringSyntaxAttribute —
// the shape of every netstandard2.0 / net4x library built with this package, since the
// generator emits an internal polyfill into each one.
//
// A consumer calling this sees an attribute whose class is a *different symbol* from its
// own StringSyntaxAttribute, so matching the two by symbol identity silently ignores the
// annotation. Referenced from IntegrationTests, which sets
// WarningsAsErrors=SSA001;SSA002;SSA003 — a false SSA003 on a correctly annotated call
// fails that build.
using System.Diagnostics.CodeAnalysis;

public static class PolyfilledApi
{
    public static void TakeJson([StringSyntax(StringSyntaxAttribute.Json)] string value)
    {
    }
}

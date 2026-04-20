// Verifies the `stringsyntax.name_conventions = enabled` knob (set via
// NameConventions.globalconfig) against the packed nupkg. Each scenario below
// would fire one of SSA001/SSA002/SSA003/SSA008 (treated as errors in the
// csproj) without the convention. Compiling clean is the test.

public static class NameConventionsSample
{
    [StringSyntax(Syntax.Uri)]
    public static string Endpoint { get; set; } = "";

    public static void ConsumeUri([StringSyntax(Syntax.Uri)] string value) { }
    public static void ConsumeHtml([StringSyntax(Syntax.Html)] string value) { }
    public static void TakeUrl(string url) { }

    // 1. Source-side promotion: parameter `url` carries no attribute, but its
    //    name matches the Uri convention, so passing it to a [Uri] target
    //    must not fire SSA002.
    public static void SourceParameter_PromotedByName(string url) =>
        ConsumeUri(url);

    // 2. Source-side promotion via PascalCase suffix: `pageHtml` -> Html.
    public static void SourceLocal_PromotedByName()
    {
        var pageHtml = "<p/>";
        ConsumeHtml(pageHtml);
    }

    // 3. Target-side promotion: target parameter `url` carries no attribute,
    //    but its name promotes it to Uri. Passing a [Uri]-attributed source
    //    into it must not fire SSA003.
    public static void TargetParameter_PromotedByName() =>
        TakeUrl(Endpoint);

    // 4. Cross-side: both source and target promote by name to the same
    //    convention. No attribute on either side, no diagnostic.
    public static void BothSidesPromoted(string url) =>
        TakeUrl(url);
}

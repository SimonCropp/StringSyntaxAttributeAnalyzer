// Human-readable "<kind> '<name>'" labels for code-fix titles. Without these,
// titles like `Add [Syntax("Regex")]` leave users guessing which declaration
// the fix targets when multiple diagnostics are visible. Walks a variant of
// the host shapes FindAttributeHost returns, plus AttributeOwner for the
// Replace fixer which starts from an AttributeSyntax instead of a host.
static class HostDescription
{
    public static string Describe(SyntaxNode host) =>
        host switch
        {
            PropertyDeclarationSyntax property =>
                $"property '{property.Identifier.Text}'",
            FieldDeclarationSyntax { Declaration.Variables.Count: > 0 } field =>
                $"field '{field.Declaration.Variables[0].Identifier.Text}'",
            ParameterSyntax parameter =>
                $"parameter '{parameter.Identifier.Text}'",
            MethodDeclarationSyntax method =>
                $"method '{method.Identifier.Text}'",
            LocalFunctionStatementSyntax local =>
                $"local function '{local.Identifier.Text}'",
            DelegateDeclarationSyntax del =>
                $"delegate '{del.Identifier.Text}'",
            LocalDeclarationStatementSyntax { Declaration.Variables.Count: > 0 } localStatement =>
                $"local '{localStatement.Declaration.Variables[0].Identifier.Text}'",
            _ => "declaration"
        };

    // Walk up from an attribute to the property/field/parameter it decorates.
    // Returns null for attribute targets we don't describe (callers then fall
    // back to a generic title).
    public static SyntaxNode? FindAttributeOwner(AttributeSyntax attribute) =>
        attribute.Parent?.Parent switch
        {
            PropertyDeclarationSyntax property => property,
            FieldDeclarationSyntax field => field,
            ParameterSyntax parameter => parameter,
            MethodDeclarationSyntax method => method,
            _ => null
        };
}

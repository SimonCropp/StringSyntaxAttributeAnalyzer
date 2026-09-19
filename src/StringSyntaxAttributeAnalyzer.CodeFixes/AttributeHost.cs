// Syntax-host classification used by the codefix to figure out *what* it's
// attaching an attribute (or language comment) to. The "host" is the syntax
// node that physically carries the attribute list — for fields and locals
// this is the enclosing declaration, not the VariableDeclaratorSyntax that
// DeclaringSyntaxReferences points at.
static class AttributeHost
{
    // Both IFieldSymbol and ILocalSymbol DeclaringSyntaxReferences point at the
    // VariableDeclaratorSyntax (e.g. `a` in `public string a, b;` or `var a = 1`).
    // The host depends on context: FieldDeclarationSyntax for a field,
    // LocalDeclarationStatementSyntax for a local. Multi-declarator forms are
    // refused in both cases — one attribute or comment would apply to all.
    public static SyntaxNode? Find(SyntaxNode node)
    {
        // Matched on the node itself, never on an ancestor. A fix site nested inside an
        // initializer has a declarator above it — the lambda parameter in
        // `Func<string, Regex> Compile = (string pattern) => new Regex(pattern);` would
        // otherwise resolve to the field `Compile`, putting the attribute on the wrong
        // declaration and leaving the warning on `pattern` in place.
        if (node is VariableDeclaratorSyntax declarator)
        {
            if (declarator.Parent is VariableDeclarationSyntax { Variables.Count: > 1 })
            {
                return null;
            }

            return declarator.FirstAncestorOrSelf<SyntaxNode>(ancestor =>
                ancestor is
                    FieldDeclarationSyntax or
                    LocalDeclarationStatementSyntax);
        }

        return node.FirstAncestorOrSelf<SyntaxNode>(ancestor =>
            ancestor is
                PropertyDeclarationSyntax or
                IndexerDeclarationSyntax or
                ParameterSyntax or
                MethodDeclarationSyntax or
                LocalFunctionStatementSyntax or
                DelegateDeclarationSyntax);
    }

    public static bool IsMethod(SyntaxNode? host) =>
        host is
            MethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            DelegateDeclarationSyntax;

    // Hosts that can carry [UnionSyntax(...)] / [ReturnSyntax(...)], or — for
    // locals — a pipe-delimited `// language=a|b` comment that expresses the same
    // union shape.
    public static bool CanHostUnion(SyntaxNode? host) =>
        host is
            PropertyDeclarationSyntax or
            IndexerDeclarationSyntax or
            FieldDeclarationSyntax or
            ParameterSyntax or
            MethodDeclarationSyntax or
            LocalFunctionStatementSyntax or
            DelegateDeclarationSyntax or
            LocalDeclarationStatementSyntax;
}

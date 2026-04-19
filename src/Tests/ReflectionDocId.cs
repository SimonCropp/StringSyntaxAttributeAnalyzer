using System.Reflection;
using System.Text;

// Builds XML documentation comment IDs from reflection metadata, matching the
// format produced by Roslyn's ISymbol.GetDocumentationCommentId() so generated
// keys round-trip exactly between this generator and the analyzer's runtime
// lookup. Spec: https://learn.microsoft.com/dotnet/csharp/language-reference/xmldoc/processing-the-xml-file
static class ReflectionDocId
{
    public static string ForMethod(MethodBase method)
    {
        var sb = new StringBuilder("M:");
        AppendMemberPrefix(sb, method);
        if (method is MethodInfo {IsGenericMethod: true} mi)
        {
            sb.Append("``").Append(mi.GetGenericArguments().Length);
        }
        AppendParameters(sb, method.GetParameters());
        return sb.ToString();
    }

    public static string ForProperty(PropertyInfo property)
    {
        var sb = new StringBuilder("P:");
        AppendMemberPrefix(sb, property);
        AppendParameters(sb, property.GetIndexParameters());
        return sb.ToString();
    }

    public static string ForField(FieldInfo field) =>
        $"F:{TypeFullName(field.DeclaringType!)}.{field.Name}";

    static void AppendMemberPrefix(StringBuilder sb, MemberInfo member)
    {
        sb.Append(TypeFullName(member.DeclaringType!));
        sb.Append('.');
        // Explicit interface implementations come back as "Namespace.IFoo.Bar" —
        // the dots in the leading qualifier are encoded as `#` in doc IDs.
        sb.Append(member.Name.Replace('.', '#'));
    }

    static void AppendParameters(StringBuilder sb, ParameterInfo[] parameters)
    {
        if (parameters.Length == 0)
        {
            return;
        }

        sb.Append('(');
        for (var i = 0; i < parameters.Length; i++)
        {
            if (i > 0)
            {
                sb.Append(',');
            }

            sb.Append(TypeRef(parameters[i].ParameterType));
        }

        sb.Append(')');
    }

    static string TypeFullName(Type type)
    {
        if (type.IsNested)
        {
            return $"{TypeFullName(type.DeclaringType!)}.{type.Name}";
        }

        return type.Namespace is null ? type.Name : $"{type.Namespace}.{type.Name}";
    }

    // Parameter / return-type position: arrays use `[]` / `[0:,0:]`, ref uses `@`,
    // pointer uses `*`, generics use `{T1,T2}`, generic params use `\`N` / `\`\`N`.
    static string TypeRef(Type type)
    {
        if (type.IsByRef)
        {
            return TypeRef(type.GetElementType()!) + "@";
        }

        if (type.IsPointer)
        {
            return TypeRef(type.GetElementType()!) + "*";
        }

        if (type.IsArray)
        {
            var rank = type.GetArrayRank();
            var inner = TypeRef(type.GetElementType()!);
            if (rank == 1)
            {
                return inner + "[]";
            }

            return inner + "[" + string.Join(",", Enumerable.Repeat("0:", rank)) + "]";
        }

        if (type.IsGenericParameter)
        {
            var prefix = type.DeclaringMethod is null ? "`" : "``";
            return prefix + type.GenericParameterPosition;
        }

        if (type.IsConstructedGenericType)
        {
            var def = type.GetGenericTypeDefinition();
            var name = StripArity(TypeFullName(def));
            var args = type.GetGenericArguments().Select(TypeRef);
            return $"{name}{{{string.Join(",", args)}}}";
        }

        return TypeFullName(type);
    }

    static string StripArity(string name)
    {
        var idx = name.LastIndexOf('`');
        return idx < 0 ? name : name.Substring(0, idx);
    }
}

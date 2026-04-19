[TestFixture]
public class KnownStringSyntaxGenerationTests
{
    // Assemblies to scan for [StringSyntax(...)] annotations. The whole point of
    // pre-computing this lookup is that these annotations rarely change, so the
    // dictionary is checked into source rather than rebuilt on each compilation.
    // Add an assembly here, re-run the test, commit the regenerated file.
    static Assembly[] assemblies =
    [
        typeof(string).Assembly,                                                       // System.Private.CoreLib
        typeof(Uri).Assembly,                                                          // System.Private.Uri (or CoreLib, depending on tfm)
        typeof(Regex).Assembly,                                                        // System.Text.RegularExpressions
        typeof(JsonDocument).Assembly,                                                 // System.Text.Json
        typeof(XmlDocument).Assembly,                                                  // System.Xml.ReaderWriter
        typeof(System.Net.Http.HttpClient).Assembly,                                   // System.Net.Http
        typeof(System.ComponentModel.DataAnnotations.RegularExpressionAttribute).Assembly, // System.ComponentModel.Annotations
        typeof(System.Data.Common.DbCommand).Assembly                                  // System.Data.Common
    ];

    const string targetFileName = "KnownStringSyntax.Generated.cs";
    const string stringSyntaxAttributeFullName =
        "System.Diagnostics.CodeAnalysis.StringSyntaxAttribute";

    [Test]
    public void GeneratedFileIsUpToDate()
    {
        var entries = new SortedDictionary<string, string>(StringComparer.Ordinal);
        var scanned = new SortedSet<string>(StringComparer.Ordinal);

        foreach (var assembly in assemblies.Distinct())
        {
            scanned.Add(assembly.GetName().Name!);
            Harvest(assembly, entries);
        }

        var generated = Render(entries, scanned);
        var path = LocateTargetFile();
        var existing = File.ReadAllText(path);

        if (existing == generated)
        {
            return;
        }

        File.WriteAllText(path, generated);
        Assert.Fail(
            $"{targetFileName} was out of date and has been rewritten. Re-run the test to confirm green, then commit.");
    }

    static void Harvest(Assembly assembly, SortedDictionary<string, string> entries)
    {
        Type[] types;
        try
        {
            types = assembly.GetTypes();
        }
        catch (ReflectionTypeLoadException ex)
        {
            types = ex.Types.Where(_ => _ is not null).ToArray()!;
        }

        foreach (var type in types)
        {
            if (type is { IsPublic: false, IsNestedPublic: false })
            {
                continue;
            }

            const BindingFlags flags =
                BindingFlags.Public |
                BindingFlags.NonPublic |
                BindingFlags.Static |
                BindingFlags.Instance |
                BindingFlags.DeclaredOnly;

            foreach (var field in type.GetFields(flags))
            {
                if (TryReadStringSyntax(field.GetCustomAttributesData(), out var v))
                {
                    entries[ReflectionDocId.ForField(field)] = v;
                }
            }

            foreach (var property in type.GetProperties(flags))
            {
                if (TryReadStringSyntax(property.GetCustomAttributesData(), out var v))
                {
                    entries[ReflectionDocId.ForProperty(property)] = v;
                }
            }

            foreach (var method in type.GetMethods(flags))
            {
                HarvestMethod(method, entries);
            }

            foreach (var ctor in type.GetConstructors(flags))
            {
                HarvestMethod(ctor, entries);
            }
        }
    }

    static void HarvestMethod(MethodBase method, SortedDictionary<string, string> entries)
    {
        var methodId = ReflectionDocId.ForMethod(method);

        foreach (var parameter in method.GetParameters())
        {
            if (TryReadStringSyntax(parameter.GetCustomAttributesData(), out var v))
            {
                entries[$"{methodId}#{parameter.Name}"] = v;
            }
        }

        if (method is MethodInfo info)
        {
            var returnAttrs = info.ReturnParameter.GetCustomAttributesData();
            if (TryReadStringSyntax(returnAttrs, out var v))
            {
                entries[$"{methodId}#return"] = v;
            }
        }
    }

    static bool TryReadStringSyntax(IList<CustomAttributeData> attributes, out string value)
    {
        foreach (var attribute in attributes)
        {
            if (attribute.AttributeType.FullName != stringSyntaxAttributeFullName)
            {
                continue;
            }

            if (attribute.ConstructorArguments.Count == 0)
            {
                continue;
            }

            if (attribute.ConstructorArguments[0].Value is string s)
            {
                value = s;
                return true;
            }
        }

        value = "";
        return false;
    }

    static string Render(SortedDictionary<string, string> entries, SortedSet<string> scanned)
    {
        var builder = new StringBuilder();

        builder.Append(
            """
            // <auto-generated>
            //   Generated by KnownStringSyntaxGenerationTests. Do not edit by hand —
            //   re-run `dotnet test src/StringSyntaxAttributeAnalyzer.slnx --filter
            //   "FullyQualifiedName~KnownStringSyntaxGenerationTests"` to refresh.
            // </auto-generated>

            using System.Collections.Generic;

            public static partial class KnownStringSyntax
            {
                // Simple names (IAssemblySymbol.Name) of every assembly inspected by the
                // generator. Used by TryLookup to short-circuit before building a doc-id
                // key — symbols from any other assembly are guaranteed misses.
                public static readonly HashSet<string> ScannedAssemblies = new(System.StringComparer.Ordinal)
                {

            """);

        foreach (var name in scanned)
        {
            builder.Append(
                $"""
                        "{Escape(name)}",

                """);
        }

        builder.Append("""
                };

                public static readonly Dictionary<string, string> Lookup = new(System.StringComparer.Ordinal)
                {

            """);

        foreach (var pair in entries)
        {
            builder.Append(
                $"""
                        ["{Escape(pair.Key)}"] = "{Escape(pair.Value)}",

                """);
        }

        builder.Append(
            """
                };
            }

            """);

        return builder.ToString().ReplaceLineEndings("\r\n");
    }

    static string Escape(string value) =>
        value.Replace("\\", @"\\").Replace("\"", "\\\"");

    static string LocateTargetFile()
    {
        var directory = new DirectoryInfo(TestContext.CurrentContext.TestDirectory);
        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "StringSyntaxAttributeAnalyzer",
                targetFileName);
            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            $"Could not locate {targetFileName} walking up from {TestContext.CurrentContext.TestDirectory}");
    }
}

using Harness.Languages;
using Harness.Languages.Duplication;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.TypeScript;

internal sealed class TypeScriptNormalizedSources : INormalizedSources
{
    private static readonly HashSet<string> Keywords = new(StringComparer.Ordinal)
    {
        "as", "async", "await", "break", "case", "catch", "class", "const", "continue", "debugger", "default",
        "delete", "do", "else", "enum", "export", "extends", "false", "finally", "for", "from", "function",
        "if", "import", "in", "instanceof", "interface", "let", "new", "null", "of", "return", "static",
        "super", "switch", "this", "throw", "true", "try", "type", "typeof", "undefined", "var", "void",
        "while", "yield", "boolean", "number", "string", "unknown", "never", "readonly", "private", "public",
    };

    public Language Language => Language.TypeScript;
    public string NothingToAnalyze => "no tracked TypeScript or JavaScript source outside generated and build-output locations";

    public (IReadOnlyList<NormalizedSource> Files, string? Failure) Read(IRepository repository)
    {
        var files = new List<NormalizedSource>();
        foreach (var entry in repository.TrackedEntries.Where(entry => TypeScriptFile.IsSource(entry.Path)))
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], failure ?? $"Could not read '{entry.Path}'.");
            }

            if (TypeScriptSources.IsGeneratedContent(text))
            {
                continue;
            }

            var (masked, regions) = TypeScriptMask.Apply(text);
            files.Add(new NormalizedSource(entry.Path, LineTokenizer.Read(masked, regions, Keywords)));
        }
        return (files, null);
    }
}

internal static class TypeScriptFile
{
    private static readonly string[] Extensions = [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"];
    private static readonly string[] Generated = [".d.ts", ".d.mts", ".d.cts", ".min.js", ".min.mjs", ".generated.ts", ".g.ts"];

    public static bool IsSource(string path) => !RepositoryLocations.IsGenerated(path)
        && Extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase))
        && !Generated.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    public static bool IsTest(string path)
    {
        var name = path[(path.LastIndexOf('/') + 1)..];
        return name.Contains(".test.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".spec.", StringComparison.OrdinalIgnoreCase)
            || name.Contains(".stories.", StringComparison.OrdinalIgnoreCase)
            || path.Split('/').Any(part => part is "__tests__" or "__mocks__" or "tests" or "e2e");
    }
}

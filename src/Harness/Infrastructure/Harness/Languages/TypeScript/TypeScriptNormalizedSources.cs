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
    public string NothingToAnalyze => "no tracked authored TypeScript or JavaScript source outside toolchain-ignored paths";

    public (IReadOnlyList<NormalizedSource> Files, string? Failure) Read(IRepository repository)
    {
        var files = new List<NormalizedSource>();
        foreach (var entry in repository.TrackedEntries.Where(entry => TypeScriptFile.IsSource(entry.Path)
            && repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored)))
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], failure ?? $"Could not read '{entry.Path}'.");
            }

            var (masked, regions) = TypeScriptMask.Apply(text);
            files.Add(new NormalizedSource(entry.Path, LineTokenizer.Read(masked, regions, Keywords)));
        }
        return (files, null);
    }
}

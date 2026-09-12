namespace Harness.Languages;

/// <summary>
/// A language the harness is able to read. The key is the whole naming convention: it is the
/// applicability answer that turns every check on that language off at once, the suffix of
/// each check measured on it, and the settings section those checks read. A second language
/// is a second instance and an analyzer, never a copy of a check.
/// </summary>
internal sealed class Language
{
    public static readonly Language CSharp = new("csharp", "C#", [".cs"]);

    public static readonly Language Yaml = new("yaml", "YAML", [".yml", ".yaml"]);

    public static readonly Language TypeScript = new(
        "typescript",
        "TypeScript",
        [".ts", ".tsx", ".mts", ".cts", ".js", ".jsx", ".mjs", ".cjs"]);

    public static readonly Language Go = new("go", "Go", [".go"]);

    /// <summary>An axis over YAML with no suffix of its own: the frame detects it by marker files.</summary>
    public static readonly Language Ansible = new("ansible", "Ansible", []);

    /// <summary>Every language the harness ships, in the order the frame lists them.</summary>
    public static readonly IReadOnlyList<Language> All = [CSharp, Yaml, TypeScript, Go, Ansible];

    private Language(string key, string name, IReadOnlyList<string> suffixes)
    {
        Key = key;
        Name = name;
        Suffixes = suffixes;
    }

    public string Key { get; }

    public string Name { get; }

    /// <summary>The file suffixes that make a tracked file a source of this language; empty for a marker-detected axis.</summary>
    public IReadOnlyList<string> Suffixes { get; }

    public string Qualify(string group) => $"{group}.{Key}";
}

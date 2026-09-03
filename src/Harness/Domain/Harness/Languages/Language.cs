namespace Harness.Languages;

/// <summary>
/// A language the harness is able to read. The key is the whole naming convention: it is the
/// applicability answer that turns every check on that language off at once, the suffix of
/// each check measured on it, and the settings section those checks read. A second language
/// is a second instance and an analyzer, never a copy of a check.
/// </summary>
internal sealed class Language
{
    public static readonly Language CSharp = new("csharp", "C#");

    public static readonly Language Yaml = new("yaml", "YAML");

    public static readonly Language TypeScript = new("typescript", "TypeScript");

    /// <summary>Every language the harness ships, in the order the frame lists them.</summary>
    public static readonly IReadOnlyList<Language> All = [CSharp, Yaml, TypeScript];

    private Language(string key, string name)
    {
        Key = key;
        Name = name;
    }

    public string Key { get; }

    public string Name { get; }

    public string Qualify(string group) => $"{group}.{Key}";
}

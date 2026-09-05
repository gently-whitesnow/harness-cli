using Harness.Repository;

namespace Harness.Checks.DotNet;

/// <summary>
/// One tracked `.editorconfig` as its sections: the glob each one addresses and the keys it
/// sets, with the line each key was read from so a finding can point at it.
/// </summary>
internal sealed record EditorConfigFile(string Path, bool IsRoot, IReadOnlyList<EditorConfigFile.Section> Sections)
{
    internal sealed record Section(string Glob, IReadOnlyList<Entry> Entries)
    {
        public bool IsGeneratedCode => Entries.Any(entry =>
            entry.Key == "generated_code" && string.Equals(entry.Value, "true", StringComparison.OrdinalIgnoreCase));

        public string? this[string key] => Entries.LastOrDefault(entry => entry.Key == key)?.Value;
    }

    internal sealed record Entry(string Key, string Value, int Line);

    public string Directory
    {
        get
        {
            var slash = Path.LastIndexOf('/');
            return slash < 0 ? string.Empty : Path[..slash];
        }
    }

    /// <summary>
    /// Adds the values in force for one path, later sections overriding earlier ones, exactly
    /// as an editor resolves them.
    /// </summary>
    public void ApplyTo(Dictionary<string, string> values, string relativePath)
    {
        foreach (var section in Sections.Where(section => EditorConfigGlob.Matches(section.Glob, relativePath)))
        {
            foreach (var entry in section.Entries)
            {
                values[entry.Key] = entry.Value;
            }
        }
    }

    public static (EditorConfigFile? File, string? Failure) Read(IRepository repository, TrackedEntry entry)
    {
        var (text, failure) = repository.ReadTrackedText(entry);
        return text is null ? (null, failure) : (Parse(entry.Path, text), null);
    }

    public static EditorConfigFile Parse(string path, string text)
    {
        var sections = new List<Section>();
        var preamble = new List<Entry>();
        string? glob = null;
        var entries = preamble;
        var lines = text.Split('\n');
        for (var index = 0; index < lines.Length; index++)
        {
            var line = lines[index].Trim();
            if (line.Length == 0 || line[0] is '#' or ';')
            {
                continue;
            }

            if (line[0] == '[' && line.EndsWith(']'))
            {
                if (glob is not null)
                {
                    sections.Add(new Section(glob, entries));
                }

                glob = line[1..^1].Trim();
                entries = [];
                continue;
            }

            var separator = line.IndexOfAny(['=', ':']);
            if (separator <= 0)
            {
                continue;
            }

            entries.Add(new Entry(
                line[..separator].Trim().ToLowerInvariant(),
                line[(separator + 1)..].Trim(),
                index + 1));
        }

        if (glob is not null)
        {
            sections.Add(new Section(glob, entries));
        }

        var isRoot = preamble.Any(entry =>
            entry.Key == "root" && string.Equals(entry.Value, "true", StringComparison.OrdinalIgnoreCase));
        return new EditorConfigFile(path, isRoot, sections);
    }
}

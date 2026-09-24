using Harness.Repository;

namespace Harness.Languages.Ansible;

/// <summary>
/// What makes a repository an Ansible one: YAML is already the yaml axis, so this axis is told
/// by markers. One judgement serves `init`, `upgrade`, `harness.coverage` and the readers.
/// </summary>
internal static class AnsibleMarkers
{
    public const string ConfigFile = "ansible.cfg";

    public const string RolesDirectory = "roles";

    public const string Description =
        "ansible.cfg at the root, roles/<name>/tasks/main.yml, playbooks/*.yml or a root playbook with `hosts:`";

    private static readonly string[] YamlSuffixes = [".yml", ".yaml"];

    public static readonly IReadOnlyList<EvidenceFile> Sources = [new(ConfigFile), new("*.yml"), new("*.yaml")];

    public static bool IsYaml(string path)
        => YamlSuffixes.Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));

    /// <summary>Generated locations, directories starting with `.` (`.venv*`, `.github/`) and molecule are not read.</summary>
    public static bool IsRead(string path)
        => !path.Split('/').SkipLast(1).Any(segment => segment.StartsWith('.') || segment == "molecule");

    public static List<string> Detect(IRepository repository, IEnumerable<TrackedEntry> candidates)
    {
        var markers = new List<string>();
        foreach (var entry in candidates.Where(entry => !entry.IsSymbolicLink && IsRead(entry.Path)).OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            if (entry.Path == ConfigFile || IsRoleTasksMain(entry.Path) || IsInPlaybooks(entry.Path) || IsRootPlaybook(repository, entry))
            {
                markers.Add(entry.Path);
            }
        }

        return markers;
    }

    public static bool IsRoleTasksMain(string path)
    {
        var segments = path.Split('/');
        return segments.Length == 4
            && segments[0] == RolesDirectory
            && segments[2] == "tasks"
            && segments[3] is "main.yml" or "main.yaml";
    }

    private static bool IsInPlaybooks(string path)
    {
        var segments = path.Split('/');
        return segments.Length == 2 && segments[0] == "playbooks" && IsYaml(path);
    }

    private static bool IsRootPlaybook(IRepository repository, TrackedEntry entry)
        => !entry.Path.Contains('/')
            && IsYaml(entry.Path)
            && repository.ReadTrackedText(entry).Text is { } text
            && IsPlaybook(text);

    /// <summary>A top-level list whose items carry `hosts:`, read lexically.</summary>
    public static bool IsPlaybook(string text)
    {
        var topLevelItem = false;
        var keyIndent = 2;
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.TrimEnd('\r');
            var content = line.TrimStart(' ');
            if (content.Length == 0 || content.StartsWith('#'))
            {
                continue;
            }

            var indent = line.Length - content.Length;
            if (indent == 0)
            {
                topLevelItem = line == "-" || line.StartsWith("- ", StringComparison.Ordinal);
                if (topLevelItem)
                {
                    content = line[1..].TrimStart();
                    keyIndent = content.Length == 0 ? 2 : line.Length - content.Length;
                    indent = keyIndent;
                }
            }

            if (topLevelItem && indent == keyIndent
                && content.StartsWith("hosts:", StringComparison.Ordinal)
                && (content.Length == 6 || char.IsWhiteSpace(content[6])))
            {
                return true;
            }
        }

        return false;
    }
}

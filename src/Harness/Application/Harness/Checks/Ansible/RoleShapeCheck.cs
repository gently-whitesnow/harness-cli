using Harness.Languages.Ansible;

namespace Harness.Checks.Ansible;

/// <summary>
/// A role is found by name and read by convention: Ansible looks for `tasks/main.yml`,
/// `defaults/main.yml` and the other directories of its vocabulary, and a file anywhere else
/// in the role is invisible to it. The check keeps the role's directory to that vocabulary.
/// </summary>
internal sealed class RoleShapeCheck(IAnsibleSources sources)
    : AnsibleSourceCheck(sources, "role-shape", "roles hold only the directories Ansible reads")
{
    private const string Readme = "README.md";

    private static readonly string[] Directories =
    [
        "tasks", "defaults", "vars", "handlers", "templates", "files", "meta", "library", "module_utils",
        "filter_plugins", "lookup_plugins", "test_plugins", "action_plugins", "tests", "molecule",
    ];

    public override string Explanation => RoleShapeExplanation.Text;

    protected override CheckEvaluation Judge(CheckContext context, IReadOnlyList<AnsibleFile> files)
    {
        var roles = Sources.RolePaths(context.Repository)
            .Select(path => path.Split('/'))
            .Where(segments => segments.Length > 2)
            .GroupBy(segments => segments[1], StringComparer.Ordinal)
            .OrderBy(role => role.Key, StringComparer.Ordinal)
            .ToList();
        if (roles.Count == 0)
        {
            return CheckEvaluation.Passed("no role under roles/ to shape");
        }

        var findings = new List<Finding>();
        foreach (var role in roles)
        {
            var root = $"{AnsibleMarkers.RolesDirectory}/{role.Key}";
            foreach (var entry in role.Select(segments => (Name: segments[2], IsFile: segments.Length == 3)).DistinctBy(entry => entry.Name).OrderBy(entry => entry.Name, StringComparer.Ordinal))
            {
                if (entry.IsFile && entry.Name != Readme)
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, $"{root}/{entry.Name}",
                        "a file directly in the role directory is invisible to Ansible; a role holds only "
                            + $"{Readme} and the directories {string.Join(", ", Directories)}"));
                }
                else if (!entry.IsFile && !Directories.Contains(entry.Name, StringComparer.Ordinal))
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, $"{root}/{entry.Name}",
                        "not a directory Ansible reads from a role; move its content under one of "
                            + $"{string.Join(", ", Directories)} or out of the role"));
                }
            }

            if (role.Any(segments => segments[2] == "tasks") && !role.Any(segments => segments.Length == 4 && segments[2] == "tasks" && segments[3] is "main.yml" or "main.yaml"))
            {
                findings.Add(new Finding(FindingSeverity.Blocking, $"{root}/tasks",
                    "has no main.yml, so the role cannot be applied by name; add tasks/main.yml as the entry point"));
            }
        }

        return CheckEvaluation.From(findings, findings.Count == 0 ? $"{roles.Count} role{(roles.Count == 1 ? "" : "s")} follow the role layout" : null);
    }
}

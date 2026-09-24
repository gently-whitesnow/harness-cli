using System.Text.RegularExpressions;
using Harness.Checks.LintSuppressions;
using Harness.Languages.Ansible;
using Harness.Repository;

namespace Harness.Checks.Ansible;

/// <summary>
/// ansible-lint holds only while nobody silences it one task at a time. A `# noqa` names the
/// rules it silences and the reason, or the finding stands; a rule switched off for the whole
/// repository in `.ansible-lint` is a decision the report prints. The linter is never run.
/// </summary>
internal sealed partial class AnsibleLintSuppressionsCheck(IAnsibleSources sources)
    : AnsibleSourceCheck(sources, "lint-suppressions", "ansible-lint is not silenced at an address")
{
    private static readonly IReadOnlyList<EvidenceFile> Configs =
        [new(".ansible-lint"), new(".ansible-lint.yml"), new(".ansible-lint.yaml")];

    private static readonly string[] RepositoryWideLists = ["skip_list", "warn_list"];

    public override IReadOnlyList<EvidenceFile> Evidence => [.. AnsibleMarkers.Sources, .. Configs];

    public override string Explanation => AnsibleLintSuppressionsExplanation.Text;

    protected override CheckEvaluation Judge(CheckContext context, IReadOnlyList<AnsibleFile> files)
    {
        var findings = new List<Finding>();
        var details = new List<string>();
        var declared = context.Config!.Settings.RepositoryWideFor(Id);
        var actual = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var file in files)
        {
            foreach (var line in file.Lines.Where(line => line.Comment is not null && !line.IsComment))
            {
                Judge($"{file.Path}:{line.Number}", line.Comment!, findings);
            }
        }

        foreach (var entry in Configs.SelectMany(context.Tracked).OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var (text, failure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return CheckEvaluation.Incomplete(failure ?? $"Could not read '{entry.Path}'.");
            }

            var root = YamlConfig.Parse(text);
            foreach (var list in RepositoryWideLists)
            {
                foreach (var rule in root.Member(list)?.Values ?? [])
                {
                    actual.Add(rule);
                    var effect = list == "skip_list" ? "skipped" : "downgraded to a warning";
                    var location = $"{entry.Path}:{root.Member(list)!.Line}";
                    if (declared.TryGetValue(rule, out var reason))
                    {
                        details.Add($"{rule} is {effect} repository-wide via {list} at {location}; declared reason: {reason}");
                    }
                    else
                    {
                        findings.Add(new Finding(FindingSeverity.Blocking, location,
                            $"{rule} is {effect} repository-wide without settings.{Id}.repositoryWide declaration"));
                    }
                }
            }

            foreach (var path in root.Member("exclude_paths")?.Values ?? [])
            {
                var normalized = path.TrimEnd('/').TrimStart('.', '/');
                var addressesCode = path is "." or "./" or "*" or ".*"
                    || path.Contains('*') || path.Contains('[') || path.Contains('(')
                    || context.Repository.TrackedEntries.Any(candidate =>
                        AnsibleMarkers.IsYaml(candidate.Path)
                        && (candidate.Path == normalized || candidate.Path.StartsWith(normalized + "/", StringComparison.Ordinal)
                            || candidate.Path == path.TrimEnd('/') || candidate.Path.StartsWith(path.TrimEnd('/') + "/", StringComparison.Ordinal)));
                if (addressesCode)
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, entry.Path,
                        $"ansible-lint exclude_paths hides code at '{path}'"));
                }
                else
                {
                    details.Add($"exclude_paths at {entry.Path} names no tracked Ansible code: {path}");
                }
            }

            foreach (var member in root.Members ?? [])
            {
                if ((member.Key.StartsWith("exclude", StringComparison.Ordinal)
                        || member.Key.StartsWith("skip", StringComparison.Ordinal)
                        || member.Key.StartsWith("warn", StringComparison.Ordinal))
                    && member.Key is not ("exclude_paths" or "skip_list" or "warn_list")
                    && !member.Value.IsEmpty)
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, entry.Path,
                        $"unrecognized ansible-lint suppression key {member.Key}"));
                }
            }
        }

        findings.AddRange(declared.Keys.Where(id => !actual.Contains(id))
            .Select(id => new Finding(FindingSeverity.Blocking, ".harness.json",
                $"stale settings.{Id}.repositoryWide entry for {id}")));

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no ansible-lint rule is silenced at an address" : null,
            details: details);
    }

    // `# noqa: rule[tag] other -- reason` or `# noqa rule # reason`; the reason follows ` -- ` or a second `#`.
    private static void Judge(string location, string comment, List<Finding> findings)
    {
        var match = Directive().Match(comment);
        if (!match.Success)
        {
            return;
        }

        var rules = match.Groups["rules"].Value.Split([',', ' '], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var reason = match.Groups["reason"].Value.Trim();
        if (rules.Length == 0)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                location,
                "silences every ansible-lint rule via a bare `# noqa`; name the rules and the reason on the same line "
                    + "(`# noqa: fqcn[action] -- reason`), or fix the task"));
        }
        else
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                location,
                $"silences {string.Join(", ", rules)} at one address via `# noqa`; "
                    + (reason.Length == 0 ? "a reason does not authorize this exception; fix the task" : "fix the task")));
        }
    }

    [GeneratedRegex(@"^noqa(?::|(?=\s)|$)\s*(?<rules>[^#]*?)\s*(?:(?:--|#)\s*(?<reason>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Directive();
}

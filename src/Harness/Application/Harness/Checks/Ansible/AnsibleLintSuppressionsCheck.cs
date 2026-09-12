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
        foreach (var file in files.Where(file => !file.IsTemplate))
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
                    var effect = list == "skip_list" ? "skipped" : "downgraded to a warning";
                    details.Add($"{rule} is {effect} repository-wide via {list} at {entry.Path}:{root.Member(list)!.Line}");
                }
            }
        }

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
        else if (reason.Length == 0)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                location,
                $"silences {string.Join(", ", rules)} via `# noqa: {string.Join(" ", rules)}` without a reason; "
                    + "add ` -- <reason>` after the rules on the same line, or fix the task"));
        }
    }

    [GeneratedRegex(@"^noqa(?::|(?=\s)|$)\s*(?<rules>[^#]*?)\s*(?:(?:--|#)\s*(?<reason>.*))?$", RegexOptions.CultureInvariant)]
    private static partial Regex Directive();
}

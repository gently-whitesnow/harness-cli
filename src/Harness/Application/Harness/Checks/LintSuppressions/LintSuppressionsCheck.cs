using System.Text.RegularExpressions;
using Harness.Languages;
using Harness.Languages.Go;
using Harness.Repository;

namespace Harness.Checks.LintSuppressions;

/// <summary>
/// A linter only means something while nobody silences it one finding at a time. This check
/// reads every place a Go repository can do that — `//nolint` directives in the source and the
/// exclusion rules of a tracked golangci-lint configuration — and applies the ADR-0035 rule to
/// the linter's findings: silencing at an address is blocking unless the directive names its
/// linters and its reason; switching a linter off for the whole repository is a decision the
/// report prints. golangci-lint itself is never located or run.
/// </summary>
internal sealed partial class LintSuppressionsCheck(IGoSources sources) : IRepositoryCheck
{
    private static readonly EvidenceFile Sources = new("*.go");

    private static readonly IReadOnlyList<EvidenceFile> Configs =
        GolangciConfig.FileNames.Select(name => new EvidenceFile(name)).ToList();

    private static readonly string[] RepositoryWidePaths = ["", ".", "./", ".*", "^.*", ".+", "^.+", ".*$", "^.*$"];

    public string Id => Language.Go.Qualify(Group);

    public string Group => "lint-suppressions";

    public string Applicability => Language.Go.Key;

    public IReadOnlyList<EvidenceFile> Evidence => [Sources, .. Configs];

    public string Summary => "Go linters are not silenced at an address";

    public string Explanation => LintSuppressionsExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable(IGoSources.NothingToAnalyze);
        }

        var findings = new List<Finding>();
        var details = new List<string>();
        foreach (var file in files)
        {
            foreach (var comment in file.Comments)
            {
                Judge(file.Path, comment, findings);
            }
        }

        foreach (var entry in Configs.SelectMany(context.Tracked).OrderBy(entry => entry.Path, StringComparer.Ordinal))
        {
            var (text, readFailure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return CheckEvaluation.Incomplete(readFailure ?? $"Could not read '{entry.Path}'.");
            }

            var (root, parseFailure) = GolangciConfig.Parse(entry.Path, text);
            if (root is null)
            {
                return CheckEvaluation.Incomplete(parseFailure!);
            }

            JudgeConfig(entry.Path, root, findings, details);
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no linter is silenced at an address" : null,
            details: details);
    }

    // The golangci-lint form: `//nolint:linter1,linter2 // reason`, no space after the slashes.
    private static void Judge(string path, GoComment comment, List<Finding> findings)
    {
        if (!Directive().IsMatch(comment.Text))
        {
            return;
        }

        var location = $"{path}:{comment.Line}";
        var explanation = comment.Text.IndexOf("//", StringComparison.Ordinal);
        var directive = explanation < 0 ? comment.Text : comment.Text[..explanation];
        var reason = explanation < 0 ? string.Empty : comment.Text[(explanation + 2)..].Trim();
        var blanket = !directive.StartsWith("nolint:", StringComparison.Ordinal)
            || directive.StartsWith("nolint:all", StringComparison.Ordinal);
        var linters = !directive.StartsWith("nolint:", StringComparison.Ordinal) ? [] : directive["nolint:".Length..]
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        if (blanket || linters.Length == 0 || linters.Contains("all", StringComparer.OrdinalIgnoreCase))
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                location,
                $"silences every linter via //nolint{(linters.Length == 0 ? "" : ":all")}; name the linters and the reason "
                    + "on the same line (`//nolint:errcheck // reason`), or fix the code"));
            return;
        }

        if (reason.Length == 0)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                location,
                $"silences {string.Join(", ", linters)} via //nolint:{string.Join(",", linters)} without a reason; "
                    + "add `// <reason>` after the directive on the same line, or fix the code"));
        }
    }

    private static void JudgeConfig(string path, ConfigNode root, List<Finding> findings, List<string> details)
    {
        var enabled = root.Get("linters", "enable") is { } enable && !enable.IsEmpty;
        if (root.Get("linters", "disable-all") is { IsTrue: true } disableAll && !enabled)
        {
            details.Add($"every linter is switched off repository-wide via linters.disable-all: true without linters.enable at {At(path, disableAll)}");
        }

        if (root.Get("linters", "default") is { Scalar: "none" } none && !enabled)
        {
            details.Add($"every linter is switched off repository-wide via linters.default: none without linters.enable at {At(path, none)}");
        }

        if (root.Get("linters", "disable") is { } disable)
        {
            foreach (var linter in disable.Values)
            {
                details.Add($"{linter} is switched off repository-wide via linters.disable at {At(path, disable)}");
            }
        }

        JudgeRules(path, root.Get("issues", "exclude-rules"), "issues.exclude-rules", findings, details);
        JudgeRules(path, root.Get("linters", "exclusions", "rules"), "linters.exclusions.rules", findings, details);
        JudgePaths(path, root.Get("issues", "exclude-dirs"), "issues.exclude-dirs", findings, details);
        JudgePaths(path, root.Get("issues", "exclude-files"), "issues.exclude-files", findings, details);
        JudgePaths(path, root.Get("linters", "exclusions", "paths"), "linters.exclusions.paths", findings, details);
    }

    // A path list takes whole places out from under every linter: the same private exception
    // as a rule with a path, written without naming a linter.
    private static void JudgePaths(string path, ConfigNode? paths, string form, List<Finding> findings, List<string> details)
    {
        foreach (var entry in paths?.Items ?? [])
        {
            var rulePath = entry.Scalar?.Trim() ?? string.Empty;
            if (IsRepositoryWide(rulePath))
            {
                var where = rulePath.Length == 0 ? "without a path" : $"for path `{rulePath}`";
                details.Add($"{form} at {At(path, entry)} excludes every linter repository-wide, {where}");
                continue;
            }

            findings.Add(new Finding(
                FindingSeverity.Blocking,
                At(path, entry),
                $"silences every linter for path `{rulePath}` via {form} at one address; fix the code, or switch the linter "
                    + "off for the whole repository in linters.disable"));
        }
    }

    private static void JudgeRules(string path, ConfigNode? rules, string form, List<Finding> findings, List<string> details)
    {
        foreach (var rule in rules?.Items ?? [])
        {
            var rulePath = rule.Member("path")?.Scalar?.Trim() ?? string.Empty;
            var linters = rule.Member("linters")?.Values ?? [];
            var text = rule.Member("text")?.Scalar;
            var subject = (linters.Count > 0 ? string.Join(", ", linters) : "every linter")
                + (text is { Length: > 0 } ? $" matching text \"{text}\"" : "");
            if ((linters.Count == 0 && string.IsNullOrEmpty(text)) || IsRepositoryWide(rulePath))
            {
                var where = rulePath.Length == 0 ? "without a path" : $"for path `{rulePath}`";
                details.Add($"{form} at {At(path, rule)} excludes {subject} repository-wide, {where}");
                continue;
            }

            findings.Add(new Finding(
                FindingSeverity.Blocking,
                At(path, rule),
                $"silences {subject} for path `{rulePath}` via {form} at one address; fix the code, or switch the linter "
                    + "off for the whole repository in linters.disable"));
        }
    }

    // A path that is absent, the root, or a mask covering every file addresses the repository.
    private static bool IsRepositoryWide(string rulePath)
        => RepositoryWidePaths.Contains(rulePath, StringComparer.Ordinal);

    private static string At(string path, ConfigNode node) => node.Line > 0 ? $"{path}:{node.Line}" : path;

    [GeneratedRegex(@"^nolint( |:|$)", RegexOptions.CultureInvariant)]
    private static partial Regex Directive();
}

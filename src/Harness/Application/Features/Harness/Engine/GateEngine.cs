using System.Diagnostics;
using Harness.Checks;
using Harness.Config;
using Harness.Git;

namespace Harness.Engine;

/// <summary>
/// Owns selection, ordering, execution, timing, policy and aggregation.
/// Callers hand it a repository and selection options; they never assemble a run themselves.
/// </summary>
/// <remarks>
/// Policy is applied here, once, rather than inside each check. A check states what it found
/// and stops; whether the repository requires it is decided in one place.
/// </remarks>
internal static class GateEngine
{
    public static RunReport Run(
        string repositoryPath,
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        var invalidSelection = InvalidSelectionReport(only, skip, checks);
        if (invalidSelection is not null)
        {
            return invalidSelection;
        }

        var (repository, failure) = GitRepository.Open(repositoryPath);
        if (repository is null)
        {
            return new RunReport(repositoryPath, [], failure);
        }

        var (config, configFailure) = HarnessConfig.Load(repository, CheckRegistry.Describe(checks));
        var invalidConfig = InvalidConfigReport(repository, config, configFailure, checks);
        if (invalidConfig is not null)
        {
            return invalidConfig;
        }

        var unexplained = new HashSet<EvidenceFile>();

        var gates = new List<GateReport>();
        foreach (var check in checks)
        {
            var policy = config?.PolicyFor(check.Id, check.Group) ?? CheckPolicy.Required;
            if (!IsSelected(check, only, skip) || policy == CheckPolicy.Off)
            {
                gates.Add(Excluded(check, policy, skip.Any(selector => Matches(check, selector))));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var disabled = config?.NotApplicable(check.Applicability);
            var evaluation = disabled is null
                ? Evaluate(check, new CheckContext(repository, config, configFailure, check))
                : CheckEvaluation.NotApplicable(
                    $"{HarnessConfig.FileName} answers `{disabled.Key}` not applicable — \"{disabled.Reason}\".");
            stopwatch.Stop();

            var gate = Judge(check, evaluation, stopwatch.Elapsed, config, policy);
            gates.Add(gate);

            if (disabled is null && LeftSomethingUnexplained(gate))
            {
                unexplained.UnionWith(check.Evidence);
            }
        }

        return new RunReport(
            repository.RootPath,
            gates,
            ToolError: null,
            repository.ReadDuration,
            Pin(config),
            UntrackedEvidence(repository, unexplained));
    }

    private static RunReport? InvalidSelectionReport(
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        var unknown = UnknownSelectors(only, skip, checks);
        return unknown.Count == 0
            ? null
            : new RunReport(
                RepositoryPath: null,
                Gates: [],
                ToolError: $"Unknown check identifier: {string.Join(", ", unknown)}. "
                    + $"Known identifiers: {string.Join(", ", checks.Select(check => check.Id))}.");
    }

    private static RunReport? InvalidConfigReport(
        GitRepository repository,
        HarnessConfig? config,
        string? configFailure,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        var configCheck = checks.FirstOrDefault(check => check.Id == "harness.config");
        if (config is not null || configFailure is null || configCheck is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var evaluation = Evaluate(
            configCheck,
            new CheckContext(repository, config, configFailure, configCheck));
        stopwatch.Stop();

        return new RunReport(
            repository.RootPath,
            [Judge(
                configCheck,
                evaluation,
                stopwatch.Elapsed,
                config,
                CheckPolicy.Required)],
            ToolError: null,
            repository.ReadDuration,
            Pin(config),
            UntrackedEvidence(repository, configCheck.Evidence.ToHashSet()));
    }

    /// <summary>
    /// Whether the check ended with a question open, and so with a place where a file Git
    /// cannot see may be the reason. A clean pass explains itself; an excluded check and one
    /// the repository answered not applicable asked nothing and are left out by the caller.
    /// </summary>
    private static bool LeftSomethingUnexplained(GateReport gate)
        => gate.Findings.Count > 0
            || gate.Outcome is CheckOutcome.Failed
                or CheckOutcome.Incomplete
                or CheckOutcome.ReadinessGap
                or CheckOutcome.NotApplicable;

    /// <summary>
    /// Named evidence that is in the working tree and not in the index. Otherwise "never
    /// written" and "written but never staged" read identically, and the second is the
    /// ordinary state of a repository being brought under the harness. The verdict is
    /// untouched; Git is asked once, and only when a question stayed open.
    /// </summary>
    private static List<string> UntrackedEvidence(GitRepository repository, HashSet<EvidenceFile> evidence)
    {
        if (evidence.Count == 0)
        {
            return [];
        }

        var (untracked, _) = repository.ReadUntrackedPaths();
        return untracked is null
            ? []
            : untracked
                .Where(path => evidence.Any(file => file.Matches(path)))
                .Order(StringComparer.Ordinal)
                .ToList();
    }

    private static string? Pin(HarnessConfig? config)
        => config is null ? null : config.TracksLatest ? "latest" : config.Version.ToString();

    private static GateReport Judge(
        IRepositoryCheck check,
        CheckEvaluation evaluation,
        TimeSpan duration,
        HarnessConfig? config,
        CheckPolicy policy)
    {
        var findings = evaluation.Findings.ToList();
        var outcome = evaluation.Outcome;
        var reason = evaluation.OutcomeReason;
        var detailed = evaluation.DetailedFindings.ToList();

        switch (policy)
        {
            case CheckPolicy.Required when FindingPolicy.ShouldRequire(findings):
                (findings, reason) = FindingPolicy.Require(findings, outcome, reason);
                detailed = FindingPolicy.RequireSeverity(detailed);
                outcome = CheckOutcome.Failed;
                break;

            case CheckPolicy.Advisory when outcome == CheckOutcome.Failed:
                findings = findings
                    .Select(FindingPolicy.Demote)
                    .ToList();
                detailed = detailed.Select(FindingPolicy.Demote).ToList();
                outcome = CheckOutcome.Passed;
                reason = $"{HarnessConfig.FileName} sets this check to advisory, so its violations are reported "
                    + "without failing the run.";
                break;

            // The repository has committed to this one, so an open question is no longer an
            // acceptable state for it.
            case CheckPolicy.Required when outcome == CheckOutcome.ReadinessGap:
                findings = [new Finding(FindingSeverity.Blocking, HarnessConfig.FileName, reason ?? "not satisfied")];
                detailed = findings.ToList();
                outcome = CheckOutcome.Failed;
                reason = "checks are required by default; use an advisory policy override to accept this gap.";
                break;
        }

        return new GateReport(
            check.Id,
            check.Summary,
            outcome,
            findings,
            detailed,
            duration,
            reason,
            evaluation.Observations);
    }

    private static GateReport Excluded(
        IRepositoryCheck check,
        CheckPolicy policy,
        bool explicitlySkipped)
        => new(
            check.Id,
            check.Summary,
            CheckOutcome.Skipped,
            [],
            [],
            TimeSpan.Zero,
            policy == CheckPolicy.Off
                ? $"{HarnessConfig.FileName} turns this check off."
                : explicitlySkipped ? "excluded by --skip." : null,
            []);

    private static CheckEvaluation Evaluate(IRepositoryCheck check, CheckContext context)
    {
        try
        {
            return check.Evaluate(context);
        }
        catch (Exception exception)
        {
            return CheckEvaluation.Incomplete($"{check.Id} failed to run: {exception.Message}");
        }
    }

    private static bool IsSelected(IRepositoryCheck check, IReadOnlyList<string> only, IReadOnlyList<string> skip)
    {
        if (skip.Any(selector => Matches(check, selector)))
        {
            return false;
        }

        return only.Count == 0 || only.Any(selector => Matches(check, selector));
    }

    private static bool Matches(IRepositoryCheck check, string selector)
        => string.Equals(check.Id, selector, StringComparison.Ordinal)
            || string.Equals(check.Group, selector, StringComparison.Ordinal)
            || string.Equals(check.Applicability, selector, StringComparison.Ordinal);

    private static List<string> UnknownSelectors(
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks)
        => only.Concat(skip)
            .Where(selector => !checks.Any(check => Matches(check, selector)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

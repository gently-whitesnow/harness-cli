using System.Diagnostics;
using Harness.Checks;
using Harness.Config;
using Harness.Repository;

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
    private const string ConfigCheckId = "harness.config";

    public static RunReport Run(
        IRepository repository,
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks,
        HarnessConfig? suppliedConfig = null)
    {
        var invalidSelection = InvalidSelectionReport(only, skip, checks);
        if (invalidSelection is not null)
        {
            return invalidSelection;
        }

        var descriptors = CheckCatalog.Describe(checks);
        var (config, configFailure) = suppliedConfig is null
            ? HarnessConfigReader.Load(repository, descriptors)
            : (suppliedConfig, (string?)null);
        var invalidConfig = InvalidConfigReport(repository, config, configFailure, checks, descriptors);
        if (invalidConfig is not null)
        {
            return invalidConfig;
        }

        var unexplained = new HashSet<EvidenceFile>();

        var gates = new List<GateReport>();
        foreach (var check in checks)
        {
            // The frame itself is always read; every other check is in the frame only when the
            // policy names it, the way an EditorConfig property nobody wrote is not applied.
            var policy = CheckPolicy.Required;
            var declared = config is null || check.Id == ConfigCheckId || config.TryPolicyFor(check.Id, out policy);
            if (!declared)
            {
                gates.Add(OutsideFrame(check, IsSelected(check, only, skip) && only.Count > 0));
                continue;
            }

            if (!IsSelected(check, only, skip) || policy == CheckPolicy.Off)
            {
                gates.Add(Excluded(check, policy, skip.Any(selector => Matches(check, selector))));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var disabled = config?.NotApplicable(check.Applicability);
            var evaluation = disabled is null
                ? Evaluate(check, new CheckContext(repository, config, configFailure, check.Id, check.Evidence))
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

    /// <summary>A run stopped at the frame, so a workspace the engine refuses reads like a frame the reader refuses.</summary>
    public static RunReport Incomplete(IRepository repository, string failure, IReadOnlyList<IRepositoryCheck> checks)
        => InvalidConfigReport(repository, null, failure, checks, CheckCatalog.Describe(checks))
            ?? new RunReport(repository.RootPath, [], ToolError: failure);

    public static RunReport Refused(string reason)
        => new(RepositoryPath: null, Gates: [], ToolError: reason);

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
        IRepository repository,
        HarnessConfig? config,
        string? configFailure,
        IReadOnlyList<IRepositoryCheck> checks,
        IReadOnlyList<CheckDescriptor> descriptors)
    {
        var configCheck = checks.FirstOrDefault(check => check.Id == ConfigCheckId);
        if (config is not null || configFailure is null || configCheck is null)
        {
            return null;
        }

        var stopwatch = Stopwatch.StartNew();
        var evaluation = Evaluate(
            configCheck,
            new CheckContext(repository, config, configFailure, configCheck.Id, configCheck.Evidence));
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
    /// Whether the check ended with a question open, so a file Git cannot see may be the reason.
    /// </summary>
    private static bool LeftSomethingUnexplained(GateReport gate)
        => gate.Findings.Count > 0
            || gate.Outcome is CheckOutcome.Failed
                or CheckOutcome.Incomplete
                or CheckOutcome.ReadinessGap
                or CheckOutcome.NotApplicable;

    /// <summary>
    /// Named evidence in the working tree but not in the index: otherwise "never written" and
    /// "written but never staged" read identically. Git is asked once, only when a question stayed open.
    /// </summary>
    private static List<string> UntrackedEvidence(IRepository repository, HashSet<EvidenceFile> evidence)
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
                outcome = outcome == CheckOutcome.Incomplete ? CheckOutcome.Incomplete : CheckOutcome.Failed;
                break;

            case CheckPolicy.Advisory when outcome is CheckOutcome.Failed or CheckOutcome.Incomplete:
                findings = findings
                    .Select(FindingPolicy.Demote)
                    .ToList();
                detailed = detailed.Select(FindingPolicy.Demote).ToList();
                if (outcome == CheckOutcome.Failed)
                {
                    outcome = CheckOutcome.Passed;
                    reason = $"{HarnessConfig.FileName} sets this check to advisory, so its violations are reported "
                        + "without failing the run.";
                }
                break;

            // The repository has committed to this one, so an open question is no longer an
            // acceptable state for it.
            case CheckPolicy.Required when outcome == CheckOutcome.ReadinessGap:
                findings = [new Finding(FindingSeverity.Blocking, HarnessConfig.FileName, reason ?? "not satisfied")];
                detailed = findings.ToList();
                outcome = CheckOutcome.Failed;
                reason = "the explicit required policy rejects this gap; choose advisory to accept it visibly.";
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
            evaluation.Details,
            Policy: PolicyName(policy));
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
            [],
            Policy: PolicyName(policy));

    /// <summary>A row only when asked for by name; otherwise one line of the summary counts it.</summary>
    private static GateReport OutsideFrame(IRepositoryCheck check, bool named)
        => new(
            check.Id,
            check.Summary,
            CheckOutcome.Skipped,
            [],
            [],
            TimeSpan.Zero,
            named
                ? $"{HarnessConfig.FileName} does not name this check in policy, so it is outside the frame; "
                    + "add a required, advisory or off entry to bring it in."
                : null,
            [],
            OutsideFrame: true);

    private static string PolicyName(CheckPolicy policy)
        => policy switch
        {
            CheckPolicy.Required => "required",
            CheckPolicy.Advisory => "advisory",
            _ => "off",
        };

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
            || (selector == "architecture" && check.Group == "architecture.sliced-dotnet")
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

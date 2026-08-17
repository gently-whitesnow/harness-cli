using System.Diagnostics;
using Harness.Checks;
using Harness.Config;
using Harness.Git;

namespace Harness.Engine;

internal sealed record GateReport(
    string Id,
    string Summary,
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    TimeSpan Duration,
    string? OutcomeReason,
    IReadOnlyList<SuppressedFinding> Suppressed);

internal sealed record RunReport(
    string? RepositoryPath,
    IReadOnlyList<GateReport> Gates,
    string? ToolError,
    TimeSpan EvidenceDuration = default)
{
    public bool NothingWasVerified
        => ToolError is not null || !Gates.Any(gate => gate.Outcome is CheckOutcome.Passed or CheckOutcome.Failed);

    public bool HasReadinessGaps => Gates.Any(gate => gate.Outcome == CheckOutcome.ReadinessGap);

    public int ExitCode
    {
        get
        {
            if (ToolError is not null || Gates.Any(gate => gate.Outcome == CheckOutcome.Incomplete))
            {
                return ExitCodes.Incomplete;
            }

            return Gates.Any(gate => gate.Outcome == CheckOutcome.Failed)
                ? ExitCodes.Violation
                : ExitCodes.Success;
        }
    }
}

internal static class ExitCodes
{
    public const int Success = 0;
    public const int Violation = 1;
    public const int Incomplete = 2;
}

/// <summary>
/// Owns selection, ordering, execution, timing, policy, suppression and aggregation.
/// Callers hand it a repository and selection options; they never assemble a run themselves.
/// </summary>
/// <remarks>
/// Policy and suppression are applied here, once, rather than inside each check. A check
/// states what it found and stops; whether the repository has agreed to live with that is a
/// different question, asked in one place, the same way for every check.
/// </remarks>
internal static class GateEngine
{
    public static RunReport Run(
        string repositoryPath,
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks)
    {
        var unknown = UnknownSelectors(only, skip, checks);
        if (unknown.Count > 0)
        {
            return new RunReport(
                RepositoryPath: null,
                Gates: [],
                ToolError: $"Unknown check identifier: {string.Join(", ", unknown)}. "
                    + $"Known identifiers: {string.Join(", ", checks.Select(check => check.Id))}.");
        }

        var (repository, failure) = GitRepository.Open(repositoryPath);
        if (repository is null)
        {
            return new RunReport(repositoryPath, [], failure);
        }

        var (config, configFailure) = HarnessConfig.Load(repository, checks);
        var context = new CheckContext(repository, config, configFailure);
        var used = new HashSet<Suppression>();

        var gates = new List<GateReport>();
        foreach (var check in checks)
        {
            var policy = config?.PolicyFor(check.Id, check.Group) ?? CheckPolicy.Default;
            if (!IsSelected(check, only, skip) || policy == CheckPolicy.Off)
            {
                gates.Add(Excluded(check, policy, skip.Any(selector => Matches(check, selector))));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var evaluation = Evaluate(check, context);
            stopwatch.Stop();

            gates.Add(Judge(check, evaluation, stopwatch.Elapsed, config, policy, used));
        }

        return new RunReport(
            repository.RootPath,
            WithStaleSuppressions(gates, config, used),
            ToolError: null,
            repository.ReadDuration);
    }

    private static GateReport Judge(
        IRepositoryCheck check,
        CheckEvaluation evaluation,
        TimeSpan duration,
        HarnessConfig? config,
        CheckPolicy policy,
        HashSet<Suppression> used)
    {
        var kept = new List<Finding>();
        var suppressed = new List<SuppressedFinding>();

        foreach (var finding in evaluation.Findings)
        {
            var exception = config?.Suppressions.FirstOrDefault(candidate => Covers(candidate, check, finding));
            if (exception is null)
            {
                kept.Add(finding);
                continue;
            }

            used.Add(exception);
            suppressed.Add(new SuppressedFinding(finding, exception));
        }

        var outcome = evaluation.Outcome;
        var reason = evaluation.OutcomeReason;

        // A check that failed only on findings the repository has accepted in writing has
        // not proved anything it does not already know. It passes, and says why it passes.
        if (outcome == CheckOutcome.Failed && !kept.Any(finding => finding.Severity == FindingSeverity.Blocking))
        {
            outcome = CheckOutcome.Passed;
            reason = $"every violation is a named exception in {HarnessConfig.FileName}; they are listed below "
                + "rather than removed.";
        }

        switch (policy)
        {
            case CheckPolicy.Advisory when outcome == CheckOutcome.Failed:
                kept = kept
                    .Select(finding => finding with { Severity = FindingSeverity.Advisory })
                    .ToList();
                outcome = CheckOutcome.Passed;
                reason = $"{HarnessConfig.FileName} sets this check to advisory, so its violations are reported "
                    + "without failing the run.";
                break;

            // The repository has committed to this one, so an open question is no longer an
            // acceptable state for it.
            case CheckPolicy.Required when outcome == CheckOutcome.ReadinessGap:
                kept = [new Finding(FindingSeverity.Blocking, HarnessConfig.FileName, reason ?? "not satisfied")];
                outcome = CheckOutcome.Failed;
                reason = $"{HarnessConfig.FileName} sets this check to required.";
                break;
        }

        return new GateReport(check.Id, check.Summary, outcome, kept, duration, reason, suppressed);
    }

    /// <summary>
    /// An exception that never matched anything is reported on the frame itself. Stale
    /// exceptions accumulate silently otherwise, and a list of accepted findings nobody has
    /// re-read is exactly the thing this mechanism is supposed to prevent.
    /// </summary>
    private static List<GateReport> WithStaleSuppressions(
        List<GateReport> gates,
        HarnessConfig? config,
        HashSet<Suppression> used)
    {
        var stale = config?.Suppressions.Where(suppression => !used.Contains(suppression)).ToList() ?? [];
        if (stale.Count == 0)
        {
            return gates;
        }

        return gates
            .Select(gate => gate.Id != "harness.config" || gate.Outcome != CheckOutcome.Passed
                ? gate
                : gate with
                {
                    Findings = stale
                        .Select(suppression => new Finding(
                            FindingSeverity.Advisory,
                            HarnessConfig.FileName,
                            $"the exception for `{suppression.Check}` at {suppression.Location} matched nothing in "
                                + $"this run (\"{suppression.Reason}\"); the finding it accepted may be gone."))
                        .ToList(),
                })
            .ToList();
    }

    private static bool Covers(Suppression suppression, IRepositoryCheck check, Finding finding)
        => (string.Equals(suppression.Check, check.Id, StringComparison.Ordinal)
                || string.Equals(suppression.Check, check.Group, StringComparison.Ordinal))
            && (string.Equals(suppression.Location, finding.Location, StringComparison.Ordinal)
                || finding.Location.StartsWith(suppression.Location + "/", StringComparison.Ordinal));

    private static GateReport Excluded(
        IRepositoryCheck check,
        CheckPolicy policy,
        bool explicitlySkipped)
        => new(
            check.Id,
            check.Summary,
            CheckOutcome.Skipped,
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
            || string.Equals(check.Group, selector, StringComparison.Ordinal);

    private static List<string> UnknownSelectors(
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks)
        => only.Concat(skip)
            .Where(selector => !checks.Any(check => Matches(check, selector)))
            .Distinct(StringComparer.Ordinal)
            .ToList();
}

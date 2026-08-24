using System.Diagnostics;
using Harness.Checks;
using Harness.Config;
using Harness.Git;

namespace Harness.Engine;

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

        var (config, configFailure) = HarnessConfig.Load(repository, CheckRegistry.Describe(checks));
        var context = new CheckContext(repository, config, configFailure);
        var used = new HashSet<Suppression>();

        var gates = new List<GateReport>();
        foreach (var check in checks)
        {
            var policy = config?.PolicyFor(check.Id, check.Group) ?? CheckPolicy.Required;
            if (!IsSelected(check, only, skip) || policy == CheckPolicy.Off)
            {
                gates.Add(Excluded(check, policy, skip.Any(selector => Matches(check, selector))));
                continue;
            }

            if (config is not null && !config.Includes(check.Since))
            {
                gates.Add(NewerThanPin(check, config));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var disabled = config?.NotApplicable(check.Applicability);
            var evaluation = disabled is null
                ? Evaluate(check, context)
                : CheckEvaluation.NotApplicable(
                    $"{HarnessConfig.FileName} answers `{disabled.Key}` not applicable — \"{disabled.Reason}\".");
            stopwatch.Stop();

            gates.Add(Judge(check, evaluation, stopwatch.Elapsed, config, policy, used));
        }

        return new RunReport(
            repository.RootPath,
            WithStaleSuppressions(gates, config, used),
            ToolError: null,
            repository.ReadDuration,
            Pin(config));
    }

    private static string? Pin(HarnessConfig? config)
        => config is null ? null : config.TracksLatest ? "latest" : config.Version.ToString();

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
                reason = "checks are required by default; use an advisory policy override to accept this gap.";
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
            && CoversLocation(suppression.Location, finding.Location);

    /// <summary>
    /// An accepted location covers the file or directory named, and any line inside it. An
    /// exception that had to name a line number would expire on the next edit above it, which
    /// would make writing one down pointless.
    /// </summary>
    private static bool CoversLocation(string accepted, string found)
        => string.Equals(accepted, found, StringComparison.Ordinal)
            || found.StartsWith(accepted + "/", StringComparison.Ordinal)
            || found.StartsWith(accepted + ":", StringComparison.Ordinal);

    /// <summary>
    /// A check the pinned release did not ship. Taking a newer binary therefore cannot add a
    /// finding on its own: the repository decides when to take one on, in a reviewable commit.
    /// </summary>
    private static GateReport NewerThanPin(IRepositoryCheck check, HarnessConfig config)
        => new(
            check.Id,
            check.Summary,
            CheckOutcome.Skipped,
            [],
            TimeSpan.Zero,
            $"introduced in harness {check.Since}; this repository pins {config.Version}. "
                + "Run `harness upgrade` to take it on.",
            []);

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

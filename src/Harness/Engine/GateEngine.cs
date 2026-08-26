using System.Diagnostics;
using Harness.Checks;
using Harness.Config;
using Harness.Git;

namespace Harness.Engine;

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

        var used = new HashSet<Suppression>();
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

            if (config is not null && !config.Includes(check.Since))
            {
                gates.Add(NewerThanPin(check, config));
                continue;
            }

            var stopwatch = Stopwatch.StartNew();
            var disabled = config?.NotApplicable(check.Applicability);
            var evaluation = disabled is null
                ? Evaluate(check, new CheckContext(repository, config, configFailure, check))
                : CheckEvaluation.NotApplicable(
                    $"{HarnessConfig.FileName} answers `{disabled.Key}` not applicable — \"{disabled.Reason}\".");
            stopwatch.Stop();

            var gate = Judge(check, evaluation, stopwatch.Elapsed, config, policy, used);
            gates.Add(gate);

            if (disabled is null && LeftSomethingUnexplained(gate))
            {
                unexplained.UnionWith(check.Evidence);
            }
        }

        return new RunReport(
            repository.RootPath,
            WithStaleSuppressions(gates, config, used),
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
                CheckPolicy.Required,
                [])],
            ToolError: null,
            repository.ReadDuration,
            Pin(config),
            UntrackedEvidence(repository, configCheck.Evidence.ToHashSet()));
    }

    /// <summary>Whether an unseen file could explain the check's open question.</summary>
    private static bool LeftSomethingUnexplained(GateReport gate)
        => gate.Findings.Count > 0
            || gate.Outcome is CheckOutcome.Failed
                or CheckOutcome.Incomplete
                or CheckOutcome.ReadinessGap
                or CheckOutcome.NotApplicable;

    /// <summary>
    /// Distinguishes missing evidence from evidence written but not staged. It does not alter
    /// the verdict, and Git is queried only while a question remains open.
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
        var detailed = evaluation.DetailedFindings.ToList();

        if (outcome == CheckOutcome.Failed && !kept.Any(finding => finding.Severity == FindingSeverity.Blocking))
        {
            outcome = CheckOutcome.Passed;
            reason = $"every violation is a named exception in {HarnessConfig.FileName}; they are listed below "
                + "rather than removed.";
        }

        switch (policy)
        {
            case CheckPolicy.Required when FindingPolicy.ShouldRequire(kept, config):
                (kept, reason) = FindingPolicy.Require(kept, outcome, reason);
                detailed = FindingPolicy.RequireSeverity(detailed);
                outcome = CheckOutcome.Failed;
                break;

            case CheckPolicy.Advisory when outcome == CheckOutcome.Failed:
                kept = kept
                    .Select(FindingPolicy.Demote)
                    .ToList();
                detailed = detailed.Select(FindingPolicy.Demote).ToList();
                outcome = CheckOutcome.Passed;
                reason = $"{HarnessConfig.FileName} sets this check to advisory, so its violations are reported "
                    + "without failing the run.";
                break;

            case CheckPolicy.Required when outcome == CheckOutcome.ReadinessGap:
                kept = [new Finding(FindingSeverity.Blocking, HarnessConfig.FileName, reason ?? "not satisfied")];
                detailed = kept.ToList();
                outcome = CheckOutcome.Failed;
                reason = "checks are required by default; use an advisory policy override to accept this gap.";
                break;
        }

        return new GateReport(check.Id, check.Summary, outcome, kept, detailed, duration, reason, suppressed);
    }

    /// <summary>Reports unmatched exceptions on the frame instead of letting them accumulate.</summary>
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

        var policy = config!.PolicyFor("harness.config", "harness");
        var required = policy == CheckPolicy.Required && FindingPolicy.UsesRequiredContract(config);
        var severity = required
            ? FindingSeverity.Blocking
            : FindingSeverity.Advisory;

        return gates
            .Select(gate => gate.Id != "harness.config" || gate.Outcome != CheckOutcome.Passed
                ? gate
                : gate with
                {
                    Outcome = required ? CheckOutcome.Failed : gate.Outcome,
                    OutcomeReason = required
                        ? "checks are required by default, so a stale named exception is a blocking violation."
                        : gate.OutcomeReason,
                    Findings = stale
                        .Select(suppression => new Finding(
                            severity,
                            HarnessConfig.FileName,
                            $"the exception for `{suppression.Check}` at {suppression.Location} matched nothing in "
                                + $"this run (\"{suppression.Reason}\"); the finding it accepted may be gone."))
                        .ToList(),
                    DetailedFindings = stale
                        .Select(suppression => new Finding(
                            severity,
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
    /// A file or directory exception covers its lines; line-number coupling would make it
    /// expire after unrelated edits above the finding.
    /// </summary>
    private static bool CoversLocation(string accepted, string found)
        => string.Equals(accepted, found, StringComparison.Ordinal)
            || found.StartsWith(accepted + "/", StringComparison.Ordinal)
            || found.StartsWith(accepted + ":", StringComparison.Ordinal);

    /// <summary>
    /// A newer binary cannot enable this check until a reviewable pin upgrade takes it on.
    /// </summary>
    private static GateReport NewerThanPin(IRepositoryCheck check, HarnessConfig config)
        => new(
            check.Id,
            check.Summary,
            CheckOutcome.Skipped,
            [],
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

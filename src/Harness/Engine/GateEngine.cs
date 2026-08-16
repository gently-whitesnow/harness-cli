using System.Diagnostics;
using Harness.Checks;
using Harness.Git;

namespace Harness.Engine;

/// <summary>One check as executed: identity, outcome, evidence and cost.</summary>
internal sealed record GateReport(
    string Id,
    string Summary,
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    TimeSpan Duration,
    string? OutcomeReason,
    IReadOnlyList<ExecutedCommand> Commands);

/// <summary>Everything a caller needs to render a run and choose an exit code.</summary>
/// <param name="EvidenceDuration">Cost of collecting the repository inventory shared by all gates.</param>
internal sealed record RunReport(
    string? RepositoryPath,
    IReadOnlyList<GateReport> Gates,
    string? ToolError,
    TimeSpan EvidenceDuration = default)
{
    /// <summary>True when no selected check actually produced evidence about the repository.</summary>
    public bool NothingWasVerified
        => ToolError is not null || !Gates.Any(gate => gate.Outcome is CheckOutcome.Passed or CheckOutcome.Failed);

    /// <summary>
    /// True when a selected gate found the stack but not the command it verifies with. It
    /// does not change the exit code — a gap is not a violation — but a run that passed
    /// what it could run must not read as a repository that has everything covered.
    /// </summary>
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
    /// <summary>Every selected applicable blocking check completed and passed.</summary>
    public const int Success = 0;

    /// <summary>At least one selected applicable blocking check proved a violation.</summary>
    public const int Violation = 1;

    /// <summary>Verification could not be completed reliably.</summary>
    public const int Incomplete = 2;
}

/// <summary>
/// Owns selection, ordering, execution, timing and aggregation. Callers hand it a
/// repository and selection options; they never assemble a run themselves.
/// </summary>
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

        var gates = new List<GateReport>();
        foreach (var check in checks)
        {
            var stopwatch = Stopwatch.StartNew();
            var evaluation = IsSelected(check, only, skip)
                ? Evaluate(check, repository)
                : new CheckEvaluation(CheckOutcome.Skipped, [], null, []);
            stopwatch.Stop();

            gates.Add(new GateReport(
                check.Id,
                check.Summary,
                evaluation.Outcome,
                evaluation.Findings,
                stopwatch.Elapsed,
                evaluation.OutcomeReason,
                evaluation.Commands));
        }

        return new RunReport(repository.RootPath, gates, ToolError: null, repository.ReadDuration);
    }

    private static CheckEvaluation Evaluate(IRepositoryCheck check, GitRepository repository)
    {
        try
        {
            return check.Evaluate(repository);
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

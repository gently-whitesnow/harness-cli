using Harness.Git;
using Harness.Processes;

namespace Harness.Checks;

internal enum CheckOutcome
{
    /// <summary>The check ran and proved the repository satisfies it.</summary>
    Passed,

    /// <summary>The check ran and proved a violation.</summary>
    Failed,

    /// <summary>The check was excluded by selection and reports no evidence.</summary>
    Skipped,

    /// <summary>
    /// The repository does not have the stack this check is about. Distinct from a failure
    /// to execute, so a heterogeneous repository stays understandable.
    /// </summary>
    NotApplicable,

    /// <summary>
    /// The repository has the stack this check is about but not the quality command the
    /// check would run. Missing infrastructure is neither a code violation nor permission
    /// for the harness to invent a repository-specific command, so it is reported as its
    /// own visible state that never reads as a pass.
    /// </summary>
    ReadinessGap,

    /// <summary>The check could not be completed reliably.</summary>
    Incomplete,
}

internal enum FindingSeverity
{
    /// <summary>Proven violation; makes the run fail.</summary>
    Blocking,

    /// <summary>Needs human judgement; never fails the run on its own.</summary>
    Advisory,
}

/// <param name="Location">Repository-relative path the finding is about.</param>
internal sealed record Finding(FindingSeverity Severity, string Location, string Message);

/// <summary>One external command a check ran, as a reader can reproduce and cost it.</summary>
internal sealed record ExecutedCommand(string DisplayCommand, int ExitCode, TimeSpan Duration)
{
    public static ExecutedCommand From(ProcessResult result)
        => new(result.DisplayCommand, result.ExitCode, result.Duration);
}

/// <summary>What a check concluded, before the engine adds timing and identity.</summary>
/// <param name="OutcomeReason">
/// What the outcome means for this repository, in one line: why the check is incomplete,
/// not applicable or a readiness gap, or what a check that passed established and did not.
/// Absent when the outcome speaks for itself.
/// </param>
/// <param name="Commands">External commands the check ran, in the order it ran them.</param>
internal sealed record CheckEvaluation(
    CheckOutcome Outcome,
    IReadOnlyList<Finding> Findings,
    string? OutcomeReason,
    IReadOnlyList<ExecutedCommand> Commands)
{
    public static CheckEvaluation From(
        IReadOnlyList<Finding> findings,
        IReadOnlyList<ExecutedCommand>? commands = null)
        => new(
            findings.Any(finding => finding.Severity == FindingSeverity.Blocking)
                ? CheckOutcome.Failed
                : CheckOutcome.Passed,
            findings,
            OutcomeReason: null,
            commands ?? []);

    /// <summary>
    /// The check ran, found nothing wrong, and has something the reader still needs to know
    /// about what it did and did not establish.
    /// </summary>
    public static CheckEvaluation Passed(string reason)
        => new(CheckOutcome.Passed, [], reason, []);

    public static CheckEvaluation Incomplete(string reason, IReadOnlyList<ExecutedCommand>? commands = null)
        => new(CheckOutcome.Incomplete, [], reason, commands ?? []);

    public static CheckEvaluation NotApplicable(string reason)
        => new(CheckOutcome.NotApplicable, [], reason, []);

    public static CheckEvaluation ReadinessGap(string reason)
        => new(CheckOutcome.ReadinessGap, [], reason, []);
}

/// <summary>
/// What one check may read while it runs: the repository evidence every check shares, and
/// the outcome of the gates that already completed in this run. The second exists so a
/// check can tell evidence it merely found from evidence a command actually executed,
/// without running that command a second time at the reader's expense.
/// </summary>
internal sealed class CheckContext(GitRepository repository)
{
    private readonly Dictionary<string, CheckOutcome> completed = new(StringComparer.Ordinal);

    public GitRepository Repository { get; } = repository;

    public void Record(string checkId, CheckOutcome outcome) => completed[checkId] = outcome;

    /// <summary>
    /// True when the named gate ran in this run and passed. A gate that was skipped,
    /// declined or never reached is not evidence of anything, so it reads as false.
    /// </summary>
    public bool Passed(string checkId)
        => completed.TryGetValue(checkId, out var outcome) && outcome == CheckOutcome.Passed;
}

/// <summary>A check as the engine sees it: identity, applicability and evaluation.</summary>
internal interface IRepositoryCheck
{
    /// <summary>Stable identifier; the contract for selection, suppression and review.</summary>
    string Id { get; }

    /// <summary>Group identifier accepted by --only and --skip alongside the check identifier.</summary>
    string Group { get; }

    /// <summary>One-line description shown in normal output.</summary>
    string Summary { get; }

    /// <summary>Rationale, evidence interpretation and remediation shown by `explain`.</summary>
    string Explanation { get; }

    CheckEvaluation Evaluate(CheckContext context);
}

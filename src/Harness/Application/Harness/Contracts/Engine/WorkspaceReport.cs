namespace Harness.Contracts.Engine;

/// <summary>
/// Every frame the run measured, root first. A workspace that fails validation is one root
/// run stopped at the frame; <c>Notes</c> say what a partial run did and did not cover.
/// </summary>
internal sealed record WorkspaceReport(
    IReadOnlyList<ScopedRunReport> Runs,
    bool IsWorkspace,
    bool IsPartial,
    IReadOnlyList<string> Notes)
{
    public int ExitCode => Runs.Any(run => run.Report.ExitCode == ExitCodes.Incomplete)
        ? ExitCodes.Incomplete
        : Runs.Any(run => run.Report.ExitCode == ExitCodes.Violation) ? ExitCodes.Violation : ExitCodes.Success;

    public bool NothingWasVerified => Runs.All(run => run.Report.NothingWasVerified);

    public bool HasReadinessGaps => Runs.Any(run => run.Report.HasReadinessGaps);
}

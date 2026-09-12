namespace Harness.Contracts.Engine;

internal sealed record WorkspaceReport(
    IReadOnlyList<ScopedRunReport> Runs,
    IReadOnlyList<string> Errors,
    bool IsWorkspace,
    bool IsPartial)
{
    public int ExitCode => Errors.Count > 0 || Runs.Any(run => run.Report.ExitCode == ExitCodes.Incomplete)
        ? ExitCodes.Incomplete
        : Runs.Any(run => run.Report.ExitCode == ExitCodes.Violation) ? ExitCodes.Violation : ExitCodes.Success;
}

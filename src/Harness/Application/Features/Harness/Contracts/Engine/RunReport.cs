namespace Harness.Contracts.Engine;

/// <param name="UntrackedEvidence">
/// Paths a finding looked for that exist in the working tree without being tracked. They
/// change no verdict; they tell the author that the file they wrote is invisible to Git.
/// </param>
internal sealed record RunReport(
    string? RepositoryPath,
    IReadOnlyList<GateReport> Gates,
    string? ToolError,
    TimeSpan EvidenceDuration = default,
    string? Pin = null,
    IReadOnlyList<string>? UntrackedEvidence = null)
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

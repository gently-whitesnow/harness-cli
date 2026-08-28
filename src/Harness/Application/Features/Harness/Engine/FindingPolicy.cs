namespace Harness.Engine;

/// <summary>Maps a check's evidence severity through the repository's policy.</summary>
internal static class FindingPolicy
{
    public static bool ShouldRequire(IReadOnlyList<Finding> findings)
        => findings.Count > 0;

    public static (List<Finding> Findings, string? Reason) Require(
        List<Finding> findings,
        CheckOutcome previousOutcome,
        string? previousReason)
    {
        var reason = previousOutcome == CheckOutcome.Passed
            ? "the explicit required policy makes every enforceable finding a blocking violation; "
                + "choose advisory explicitly while the repository is paying down known findings."
            : previousReason;
        return (RequireSeverity(findings), reason);
    }

    public static List<Finding> RequireSeverity(IEnumerable<Finding> findings)
        => findings
            .Select(finding => finding with { Severity = FindingSeverity.Blocking })
            .ToList();

    public static Finding Demote(Finding finding)
        => finding.Severity == FindingSeverity.Blocking
            ? finding with { Severity = FindingSeverity.Advisory }
            : finding;
}

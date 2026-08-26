using Harness.Checks;
using Harness.Config;
using Harness.Versioning;

namespace Harness.Engine;

/// <summary>Maps a check's evidence severity through the repository's versioned policy.</summary>
internal static class FindingPolicy
{
    private static readonly HarnessVersion RequiredFindingsSince = new(1, 3, 0);

    public static bool ShouldRequire(IReadOnlyList<Finding> findings, HarnessConfig? config)
        => findings.Any(finding => finding.Severity != FindingSeverity.Observation)
            && UsesRequiredContract(config);

    public static bool UsesRequiredContract(HarnessConfig? config)
        => config?.Includes(RequiredFindingsSince) == true;

    public static (List<Finding> Findings, string? Reason) Require(
        List<Finding> findings,
        CheckOutcome previousOutcome,
        string? previousReason)
    {
        var reason = previousOutcome == CheckOutcome.Passed
            ? "checks are required by default, so every enforceable finding is a blocking violation; "
                + "use an advisory policy override while the repository is paying down known findings."
            : previousReason;
        return (RequireSeverity(findings), reason);
    }

    public static List<Finding> RequireSeverity(IEnumerable<Finding> findings)
        => findings
            .Select(finding => finding.Severity == FindingSeverity.Observation
                ? finding
                : finding with { Severity = FindingSeverity.Blocking })
            .ToList();

    public static Finding Demote(Finding finding)
        => finding.Severity == FindingSeverity.Blocking
            ? finding with { Severity = FindingSeverity.Advisory }
            : finding;
}

using Harness.Config;

namespace Harness.Checks;

internal static class CheckCatalog
{
    public static IReadOnlyList<CheckDescriptor> Describe(IReadOnlyList<IRepositoryCheck> checks)
        => checks
            .Select(check => new CheckDescriptor(
                check.Id,
                check.Group,
                check.Applicability))
            .ToList();

    public static IReadOnlyList<CheckSummary> Summaries(IReadOnlyList<IRepositoryCheck> checks)
        => checks.Select(check => new CheckSummary(check.Id, check.Group, check.Summary)).ToList();
}

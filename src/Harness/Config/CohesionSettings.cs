namespace Harness.Config;

internal sealed record CohesionSettings(int MinimumMembers, int Groups)
{
    public static CohesionSettings Default { get; } = new(MinimumMembers: 6, Groups: 1);
}

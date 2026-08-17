using Harness.Commits;

namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    MaintainabilitySettings Maintainability,
    DependencySettings Dependencies,
    CohesionSettings Cohesion,
    CommitSettings Commits)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        MaintainabilitySettings.Default,
        DependencySettings.Default,
        CohesionSettings.Default,
        CommitSettings.Default);
}

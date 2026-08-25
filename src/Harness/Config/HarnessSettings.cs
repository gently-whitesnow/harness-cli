using Harness.Commits;

namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    MaintainabilitySettings Maintainability,
    DependencySettings Dependencies,
    CohesionSettings Cohesion,
    DuplicationSettings Duplication,
    CommitSettings Commits)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        MaintainabilitySettings.Default,
        DependencySettings.Default,
        CohesionSettings.Default,
        DuplicationSettings.Default,
        CommitSettings.Default);
}

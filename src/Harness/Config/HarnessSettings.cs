namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    MaintainabilitySettings Maintainability,
    CommitSettings Commits)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        MaintainabilitySettings.Default,
        CommitSettings.Default);
}

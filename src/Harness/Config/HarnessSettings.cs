namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    MaintainabilitySettings Maintainability)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        MaintainabilitySettings.Default);
}

namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    DuplicationSettings Duplication,
    CommitSettings Commits,
    WarningSuppressionSettings WarningSuppressions)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        DuplicationSettings.Default,
        CommitSettings.Default,
        WarningSuppressionSettings.Default);
}

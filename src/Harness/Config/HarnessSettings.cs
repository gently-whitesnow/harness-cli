using Harness.Commits;

namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    DuplicationSettings Duplication,
    CommitSettings Commits)
{
    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        DuplicationSettings.Default,
        CommitSettings.Default);
}

using Harness.Commits;
using Harness.Versioning;

namespace Harness.Config;

internal sealed record HarnessSettings(
    CommentSettings Comments,
    DependencySettings Dependencies,
    DuplicationSettings Duplication,
    CommitSettings Commits)
{
    private static readonly HarnessVersion RecalibratedIn = new(1, 4, 0);
    private static readonly CommitSettings LegacyCommits = new(CommitLanguage.English, RequireSetup: false);

    private static readonly HarnessSettings Legacy = new(
        new CommentSettings(MinimumCommentLines: 10, PercentageLimit: 25),
        DependencySettings.Default,
        new DuplicationSettings(WindowLines: 8, MinimumTokens: 24),
        LegacyCommits);

    public static HarnessSettings Default { get; } = new(
        CommentSettings.Default,
        DependencySettings.Default,
        DuplicationSettings.Default,
        CommitSettings.Default);

    /// <summary>Defaults are part of the repository pin, not the installed binary.</summary>
    public static HarnessSettings For(HarnessVersion version)
        => version < RecalibratedIn ? Legacy : Default;
}

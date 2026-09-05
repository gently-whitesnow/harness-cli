using Harness.Languages;

namespace Harness.Config;

internal sealed record HarnessSettings(
    IReadOnlyDictionary<string, CommentSettings> Comments,
    DuplicationSettings Duplication,
    CommitSettings Commits)
{
    public static HarnessSettings Default { get; } = new(
        Language.All.ToDictionary(language => language.Key, _ => CommentSettings.Default, StringComparer.Ordinal),
        DuplicationSettings.Default,
        CommitSettings.Default);

    public CommentSettings CommentsFor(Language language) => Comments[language.Key];
}

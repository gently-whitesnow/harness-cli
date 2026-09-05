using Harness.Repository;

namespace Harness.Languages.Comments;

/// <summary>
/// What a language supplies for the comment density check to run on it: which tracked files
/// belong to the language and how many of their lines are comments. The check never sees
/// syntax, so a second language is a reader, not a second check.
/// </summary>
internal interface ICommentedSources
{
    Language Language { get; }

    /// <summary>Why the repository has nothing for this language to read.</summary>
    string NothingToAnalyze { get; }

    (IReadOnlyList<CommentedSource> Files, string? Failure) Read(IRepository repository);
}

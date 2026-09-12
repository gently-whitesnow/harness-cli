using Harness.Repository;

namespace Harness.Languages.Duplication;

/// <summary>
/// What a language supplies for the duplication check to run on it: which tracked files
/// belong to the language and what each of their lines reads as once names and literals are
/// erased. The check never sees syntax, so a second language is a reader, not a second check.
/// </summary>
internal interface INormalizedSources
{
    Language Language { get; }

    /// <summary>Why the repository has nothing for this language to read.</summary>
    string NothingToAnalyze { get; }

    (IReadOnlyList<NormalizedSource> Files, string? Failure) Read(IRepository repository);
}

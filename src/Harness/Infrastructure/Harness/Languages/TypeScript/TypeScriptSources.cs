using Harness.Languages;
using Harness.Languages.Comments;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.TypeScript;

/// <summary>
/// The tracked TypeScript and JavaScript a repository ships, read once for the whole run.
/// Declaration files and minified bundles are products of a build, not prose an author
/// chose to keep, so they are excluded the way generated C# is.
/// </summary>
internal sealed class TypeScriptSources : ICommentedSources
{
    private IRepository? read;
    private (IReadOnlyList<CommentedSource> Files, string? Failure) result;

    public Language Language => Language.TypeScript;

    public string NothingToAnalyze =>
        "no tracked TypeScript or JavaScript source outside generated and build-output locations";

    public (IReadOnlyList<CommentedSource> Files, string? Failure) Read(IRepository repository)
    {
        if (!ReferenceEquals(read, repository))
        {
            result = Discover(repository);
            read = repository;
        }

        return result;
    }

    private static (IReadOnlyList<CommentedSource> Files, string? Failure) Discover(IRepository repository)
    {
        var candidates = repository.TrackedEntries
            .Where(entry => TypeScriptFile.IsSource(entry.Path)
                && repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal);

        var files = new List<CommentedSource>();
        foreach (var entry in candidates)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], failure ?? $"Could not read '{entry.Path}'.");
            }

            var (comments, authored) = TypeScriptCommentLines.Count(text);
            files.Add(new CommentedSource(entry.Path, comments, authored));
        }

        return (files, null);
    }

}

using Harness.Languages;
using Harness.Languages.Comments;
using Harness.Languages.Go;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Go;

/// <summary>The Go side of the comment density check, read through the shared Go reader.</summary>
internal sealed class GoCommentedSources(IGoSources sources) : ICommentedSources
{
    public Language Language => Language.Go;

    public string NothingToAnalyze => IGoSources.NothingToAnalyze;

    public (IReadOnlyList<CommentedSource> Files, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return (files.Select(file => new CommentedSource(file.Path, file.CommentLines, file.AuthoredLines)).ToList(), failure);
    }
}

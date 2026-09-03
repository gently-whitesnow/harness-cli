using Harness.Languages;
using Harness.Languages.Comments;
using Harness.Languages.CSharp;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>The C# side of the comment density check, read through the shared C# reader.</summary>
internal sealed class CSharpCommentedSources(ICSharpSources sources) : ICommentedSources
{
    public Language Language => Language.CSharp;

    public string NothingToAnalyze => ICSharpSources.NothingToAnalyze;

    public (IReadOnlyList<CommentedSource> Files, string? Failure) Read(IRepository repository)
    {
        var (files, failure) = sources.Read(repository);
        return (
            files.Select(file => new CommentedSource(file.Path, file.Source.CommentLines, file.Source.AuthoredLines))
                .ToList(),
            failure);
    }
}

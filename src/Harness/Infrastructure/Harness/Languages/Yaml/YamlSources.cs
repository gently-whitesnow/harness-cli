using Harness.Languages;
using Harness.Languages.Comments;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Yaml;

/// <summary>The tracked YAML a repository ships, read once for the whole run.</summary>
internal sealed class YamlSources : ICommentedSources
{
    private static readonly string[] Extensions = [".yml", ".yaml"];

    private IRepository? read;
    private (IReadOnlyList<CommentedSource> Files, string? Failure) result;

    public Language Language => Language.Yaml;

    public string NothingToAnalyze => "no tracked YAML outside generated and build-output locations";

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
            .Where(entry => Extensions.Any(extension =>
                entry.Path.EndsWith(extension, StringComparison.OrdinalIgnoreCase)))
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal);

        var files = new List<CommentedSource>();
        foreach (var entry in candidates)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], failure ?? $"Could not read '{entry.Path}'.");
            }

            var (comments, authored) = YamlCommentLines.Count(text);
            files.Add(new CommentedSource(entry.Path, comments, authored));
        }

        return (files, null);
    }
}

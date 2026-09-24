using Harness.Languages.CSharp;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.CSharp;

/// <summary>
/// The tracked C# a repository ships, discovered and read once for the whole run. Every C#
/// check asks the same question of the same files, and reading them once per check would
/// spend the work again for an answer that cannot have changed. What is handed out is read
/// only, so sharing it shares a reading and not a state.
/// </summary>
internal sealed class CSharpSources : ICSharpSources
{
    private IRepository? read;
    private (IReadOnlyList<CSharpFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) result;

    public (IReadOnlyList<CSharpFile> Files, string? Failure) Read(IRepository repository)
    {
        var (files, _, failure) = Discover(repository);
        return (files, failure);
    }

    /// <summary>Tracked C# in authored locations that a leading auto-generated marker excluded.</summary>
    public IReadOnlyList<string> MarkedGenerated(IRepository repository)
        => Discover(repository).MarkedGenerated;

    private (IReadOnlyList<CSharpFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) Discover(
        IRepository repository)
    {
        if (!ReferenceEquals(read, repository))
        {
            result = Read(repository.TrackedEntries, repository);
            read = repository;
        }

        return result;
    }

    private static (IReadOnlyList<CSharpFile> Files, IReadOnlyList<string> MarkedGenerated, string? Failure) Read(
        IReadOnlyList<TrackedEntry> entries,
        IRepository repository)
    {
        var candidates = entries
            .Where(entry => entry.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase))
            .Where(entry => repository.Classify(entry) is not (EvidenceKind.DeclaredGenerated or EvidenceKind.ToolchainIgnored))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal);

        var files = new List<CSharpFile>();
        var marked = entries.Where(entry => entry.Path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase)
                && repository.Classify(entry) == EvidenceKind.DeclaredGenerated)
            .Select(entry => entry.Path).ToList();
        foreach (var entry in candidates)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return ([], [], failure ?? $"Could not read '{entry.Path}'.");
            }

            var (masked, regions) = CSharpMask.Apply(text);

            var source = CSharpSource.Create(entry.Path, masked, regions);
            files.Add(new CSharpFile(source, () => CSharpStructureReader.Read(source)));
        }

        return (files, marked, null);
    }

}

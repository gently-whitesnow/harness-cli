namespace Harness.Repository;

/// <summary>
/// One frame's view of the index: a project owns the entries under its directory, the root
/// owns everything outside the registered projects, and a named file the toolchain resolves by
/// walking up stays readable through <see cref="Ancestors"/> without becoming inventory.
/// </summary>
internal sealed class ScopedRepository : IRepository
{
    private readonly IRepository repository;
    private readonly string project;
    private readonly IReadOnlyList<string> excluded;
    private readonly Dictionary<string, TrackedEntry> byPath;
    private readonly Dictionary<string, TrackedEntry> sources;
    private readonly Dictionary<string, IReadOnlyList<TrackedEntry>> ancestors = new(StringComparer.Ordinal);

    public ScopedRepository(IRepository repository, string project, IReadOnlyList<string>? excluded = null)
    {
        this.repository = repository;
        this.project = project;
        this.excluded = excluded ?? [];
        RootPath = project.Length == 0 ? repository.RootPath : Path.Combine(repository.RootPath, project);

        // The index is read once into a map so scoping does not rescan it per lookup; a path
        // repeated by the index (an unresolved merge keeps several stages) is one entry here.
        byPath = new Dictionary<string, TrackedEntry>(StringComparer.Ordinal);
        foreach (var entry in repository.TrackedEntries)
        {
            byPath.TryAdd(entry.Path, entry);
        }

        sources = new Dictionary<string, TrackedEntry>(StringComparer.Ordinal);
        var owned = new List<TrackedEntry>();
        foreach (var entry in byPath.Values.Where(entry => Owns(entry.Path)))
        {
            var local = Local(entry.Path);
            sources[local] = entry;
            owned.Add(entry with { Path = local });
        }

        TrackedEntries = owned;
    }

    public string RootPath { get; }

    public IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    public TimeSpan ReadDuration => repository.ReadDuration;

    public IReadOnlyList<TrackedEntry> Ancestors(string fileName)
    {
        if (project.Length == 0)
        {
            return [];
        }

        if (ancestors.TryGetValue(fileName, out var known))
        {
            return known;
        }

        var found = new List<TrackedEntry>();
        var directory = project;
        var prefix = string.Empty;
        while (directory.Length > 0)
        {
            var slash = directory.LastIndexOf('/');
            directory = slash < 0 ? string.Empty : directory[..slash];
            prefix += "../";
            var path = directory.Length == 0 ? fileName : directory + "/" + fileName;
            if (byPath.TryGetValue(path, out var entry))
            {
                sources[prefix + fileName] = entry;
                found.Add(entry with { Path = prefix + fileName });
            }
        }

        ancestors[fileName] = found;
        return found;
    }

    public (IReadOnlyList<(string ObjectId, string Message)>? Commits, string? Failure) ReadCommits(string revisionRange)
        => repository.ReadCommits(revisionRange);

    public (IReadOnlyList<string>? Paths, string? Failure) ReadUntrackedPaths()
    {
        var (paths, failure) = repository.ReadUntrackedPaths();
        return (paths?.Where(Owns).Select(Local).ToList(), failure);
    }

    public (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry)
        => sources.TryGetValue(entry.Path, out var source)
            ? repository.ReadSymbolicLinkTarget(source)
            : (null, $"'{entry.Path}' is outside this project's tracked evidence");

    public (string? Text, string? Failure) ReadTrackedText(TrackedEntry entry)
        => sources.TryGetValue(entry.Path, out var source)
            ? repository.ReadTrackedText(source)
            : (null, $"'{entry.Path}' is outside this project's tracked evidence");

    private bool Owns(string path)
        => (project.Length == 0 || path.StartsWith(project + "/", StringComparison.Ordinal))
            && !excluded.Any(directory => path.StartsWith(directory + "/", StringComparison.Ordinal));

    private string Local(string path) => project.Length == 0 ? path : path[(project.Length + 1)..];
}

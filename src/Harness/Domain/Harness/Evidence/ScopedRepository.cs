namespace Harness.Repository;

/// <summary>A project's inventory is local; named ancestor build evidence stays readable.</summary>
internal sealed class ScopedRepository : IRepository
{
    private readonly IRepository repository;
    private readonly string project;
    private readonly IReadOnlyList<string> excluded;
    private readonly Dictionary<string, TrackedEntry> original;

    public ScopedRepository(IRepository repository, string project, IReadOnlyList<string>? excluded = null)
    {
        this.repository = repository;
        this.project = project;
        this.excluded = excluded ?? [];
        RootPath = project.Length == 0 ? repository.RootPath : Path.Combine(repository.RootPath, project);
        original = repository.TrackedEntries.Where(entry => Owns(entry.Path))
            .ToDictionary(entry => Local(entry.Path), StringComparer.Ordinal);
        TrackedEntries = original.Select(pair => pair.Value with { Path = pair.Key }).ToList();
        var ancestors = new List<TrackedEntry>();
        if (project.Length > 0)
        {
            var directory = project;
            var prefix = string.Empty;
            while (directory.Length > 0)
            {
                var slash = directory.LastIndexOf('/');
                directory = slash < 0 ? string.Empty : directory[..slash];
                prefix += "../";
                foreach (var name in new[] { ".editorconfig", "Directory.Build.props", "Directory.Build.targets", "Directory.Packages.props" })
                {
                    var path = directory.Length == 0 ? name : directory + "/" + name;
                    var entry = repository.TrackedEntries.FirstOrDefault(candidate => candidate.Path == path);
                    if (entry is not null)
                    {
                        original[prefix + name] = entry;
                        ancestors.Add(entry with { Path = prefix + name });
                    }
                }
            }
        }

        AncestorEvidence = ancestors;
    }

    public string RootPath { get; }

    public IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    public IReadOnlyList<TrackedEntry> AncestorEvidence { get; }

    public TimeSpan ReadDuration => repository.ReadDuration;

    public (IReadOnlyList<(string ObjectId, string Message)>? Commits, string? Failure) ReadCommits(string revisionRange)
        => repository.ReadCommits(revisionRange);

    public (IReadOnlyList<string>? Paths, string? Failure) ReadUntrackedPaths()
    {
        var (paths, failure) = repository.ReadUntrackedPaths();
        return (paths?.Where(Owns).Select(Local).ToList(), failure);
    }

    public (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry)
        => original.TryGetValue(entry.Path, out var source)
            ? repository.ReadSymbolicLinkTarget(source)
            : (null, $"'{entry.Path}' is outside this project's tracked evidence");

    public (string? Text, string? Failure) ReadTrackedText(TrackedEntry entry)
        => original.TryGetValue(entry.Path, out var source)
            ? repository.ReadTrackedText(source)
            : (null, $"'{entry.Path}' is outside this project's tracked evidence");

    private bool Owns(string path)
        => (project.Length == 0 || path.StartsWith(project + "/", StringComparison.Ordinal))
            && !excluded.Any(directory => path.StartsWith(directory + "/", StringComparison.Ordinal));

    private string Local(string path) => project.Length == 0 ? path : path[(project.Length + 1)..];
}

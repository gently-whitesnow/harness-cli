using Harness.Languages.Ansible;
using Harness.Repository;

namespace Harness.Infrastructure.Languages.Ansible;

/// <summary>
/// The tracked YAML and Jinja templates of an Ansible repository, read once for the whole run.
/// Without a marker the repository is not an Ansible one and nothing is read, so a Helm chart
/// or a workflow directory never gets Ansible findings.
/// </summary>
internal sealed class AnsibleSources : IAnsibleSources
{
    private static readonly string[] Suffixes = [".yml", ".yaml", ".j2"];

    private IRepository? read;
    private Reading result = new([], [], [], null);

    public (IReadOnlyList<AnsibleFile> Files, string? Failure) Read(IRepository repository)
    {
        var reading = Discover(repository);
        return (reading.Files, reading.Failure);
    }

    public IReadOnlyList<string> Markers(IRepository repository) => Discover(repository).Markers;

    public IReadOnlyList<string> RolePaths(IRepository repository) => Discover(repository).RolePaths;

    private Reading Discover(IRepository repository)
    {
        if (!ReferenceEquals(read, repository))
        {
            result = ReadAll(repository);
            read = repository;
        }

        return result;
    }

    private static Reading ReadAll(IRepository repository)
    {
        var tracked = repository.TrackedEntries
            .Where(entry => !entry.IsSymbolicLink && AnsibleMarkers.IsRead(entry.Path))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        var markers = AnsibleMarkers.Detect(repository, tracked.Where(entry => AnsibleMarkers.Sources.Any(source => source.Matches(entry.Path))));
        if (markers.Count == 0)
        {
            return new Reading([], [], [], null);
        }

        var files = new List<AnsibleFile>();
        foreach (var entry in tracked.Where(entry => Suffixes.Any(suffix => entry.Path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase))))
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                return new Reading([], markers, [], failure ?? $"Could not read '{entry.Path}'.");
            }

            files.Add(new AnsibleFile(entry.Path, AnsibleLineReader.Read(text)));
        }

        var roles = tracked
            .Select(entry => entry.Path)
            .Where(path => path.StartsWith(AnsibleMarkers.RolesDirectory + "/", StringComparison.Ordinal))
            .ToList();
        return new Reading(files, markers, roles, null);
    }

    private sealed record Reading(
        IReadOnlyList<AnsibleFile> Files,
        IReadOnlyList<string> Markers,
        IReadOnlyList<string> RolePaths,
        string? Failure);
}

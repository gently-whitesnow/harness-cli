using Harness.Git;

namespace Harness.Checks.Surfaces;

internal enum DotNetSurfaceKind
{
    /// <summary>No tracked .NET evidence at all.</summary>
    Absent,

    /// <summary>There is .NET evidence, and it does not settle what the repository is.</summary>
    Ambiguous,

    /// <summary>Discovery read a definite .NET surface.</summary>
    Present,
}

/// <summary>
/// What Git says the repository's .NET side is: its tracked projects, and which of them
/// declare a test framework. Nothing here is configured and nothing is executed — the
/// harness reads this only to hold a declaration against what the repository visibly is,
/// so a frame cannot claim the absence of something Git plainly shows.
/// </summary>
/// <param name="Solutions">Tracked solution files; a repository's own statement of what belongs together.</param>
/// <param name="Projects">Every tracked project file.</param>
/// <param name="TestProjects">The tracked projects that declare a .NET test framework.</param>
/// <param name="Reason">Why the surface is absent or ambiguous; absent when it is present.</param>
internal sealed record DotNetSurface(
    DotNetSurfaceKind Kind,
    IReadOnlyList<string> Solutions,
    IReadOnlyList<TrackedEntry> Projects,
    IReadOnlyList<string> TestProjects,
    string? Reason)
{
    private static readonly string[] SolutionExtensions = [".sln", ".slnx"];

    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    /// <summary>Standard .NET evidence that is not itself buildable.</summary>
    private static readonly string[] ToolManifests = ["global.json", ".config/dotnet-tools.json", "nuget.config"];

    /// <summary>
    /// Evidence a project is a test project. Both are the conventional markers the SDK
    /// itself uses, so detection does not depend on a naming convention.
    /// </summary>
    public static readonly string[] TestProjectMarkers = ["Microsoft.NET.Test.Sdk", "<IsTestProject>true"];

    public static DotNetSurface Discover(GitRepository repository)
    {
        var tracked = repository.TrackedEntries
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .ToList();

        var solutions = Sorted(tracked.Where(entry => HasExtension(entry.Path, SolutionExtensions)));
        var projects = tracked.Where(entry => HasExtension(entry.Path, ProjectExtensions)).ToList();
        var manifests = tracked
            .Where(entry => ToolManifests.Contains(entry.Path, StringComparer.OrdinalIgnoreCase))
            .Select(entry => entry.Path)
            .ToList();

        if (solutions.Count == 0 && projects.Count == 0)
        {
            return Absent(
                manifests.Count > 0
                    ? $"tracked .NET tool evidence ({string.Join(", ", manifests)}) but no solution or project"
                    : "no tracked solution, project or .NET tool manifest");
        }

        // Unreadable project text is the one place discovery genuinely does not know, and
        // saying so is better than reading it as a project that carries nothing.
        var unreadable = projects.Where(entry => repository.ReadTrackedText(entry).Text is null).ToList();
        if (unreadable.Count > 0)
        {
            return Ambiguous(
                $"{unreadable.Count} tracked .NET project{(unreadable.Count == 1 ? "" : "s")} "
                    + $"({string.Join(", ", unreadable.Take(3).Select(entry => entry.Path))}) could not be read, so "
                    + "what the repository declares cannot be held against them");
        }

        return new DotNetSurface(
            DotNetSurfaceKind.Present,
            solutions,
            projects.OrderBy(entry => entry.Path, StringComparer.Ordinal).ToList(),
            Sorted(projects.Where(entry => IsTestProject(repository, entry))),
            Reason: null);
    }

    /// <summary>
    /// Tracked projects that carry one of the given package or marker names, each paired
    /// with the first name it carries, so the report can say what it recognized rather than
    /// only that it recognized something.
    /// </summary>
    public IReadOnlyList<(string Path, string Name)> ProjectsCarrying(
        GitRepository repository,
        IReadOnlyList<string> names)
    {
        var carriers = new List<(string, string)>();
        foreach (var entry in Projects)
        {
            var (text, _) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                continue;
            }

            var carried = names.FirstOrDefault(name => text.Contains(name, StringComparison.OrdinalIgnoreCase));
            if (carried is not null)
            {
                carriers.Add((entry.Path, carried));
            }
        }

        return carriers;
    }

    private static DotNetSurface Absent(string reason)
        => new(DotNetSurfaceKind.Absent, [], [], [], reason);

    private static DotNetSurface Ambiguous(string reason)
        => new(DotNetSurfaceKind.Ambiguous, [], [], [], reason);

    private static bool IsTestProject(GitRepository repository, TrackedEntry entry)
    {
        var (text, _) = repository.ReadTrackedText(entry);
        return text is not null
            && TestProjectMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExtension(string path, string[] extensions)
        => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static List<string> Sorted(IEnumerable<TrackedEntry> entries)
        => entries.Select(entry => entry.Path).OrderBy(path => path, StringComparer.Ordinal).ToList();
}

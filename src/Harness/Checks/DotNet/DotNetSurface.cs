using Harness.Git;

namespace Harness.Checks.DotNet;

internal enum DotNetSurfaceKind
{
    /// <summary>No tracked .NET evidence at all; the .NET gates do not apply.</summary>
    Absent,

    /// <summary>There is .NET evidence, but no single defensible execution plan.</summary>
    Ambiguous,

    /// <summary>Discovery produced a definite plan the gates can execute.</summary>
    Present,
}

/// <summary>
/// What the .NET gates should run against, derived only from Git-tracked evidence:
/// solutions, project files and the standard tool manifests that accompany them. Nothing
/// here is configured; a repository needs no harness manifest to be discovered. When the
/// evidence does not single out a plan, discovery says so instead of picking one.
/// </summary>
/// <param name="BuildTargets">Repository-relative targets for formatting and compilation.</param>
/// <param name="TestTargets">Repository-relative targets that hold test projects.</param>
/// <param name="HasLockFiles">Whether a tracked NuGet lock file constrains restore.</param>
/// <param name="Reason">Why the surface is absent or ambiguous; absent when it is present.</param>
internal sealed record DotNetSurface(
    DotNetSurfaceKind Kind,
    IReadOnlyList<string> BuildTargets,
    IReadOnlyList<string> TestTargets,
    bool HasLockFiles,
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
    private static readonly string[] TestProjectMarkers = ["Microsoft.NET.Test.Sdk", "<IsTestProject>true"];

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

        if (solutions.Count > 1)
        {
            return Ambiguous(
                $"{solutions.Count} tracked solution files ({string.Join(", ", solutions)}) do not single out "
                    + "one execution plan; the .NET gates will not guess which one represents the repository.");
        }

        if (solutions.Count == 0 && projects.Count == 0)
        {
            return Absent(
                manifests.Count > 0
                    ? $"tracked .NET tool evidence ({string.Join(", ", manifests)}) but no solution or project to "
                        + "format, build or test"
                    : "no tracked solution, project or .NET tool manifest");
        }

        var hasLockFiles = tracked.Any(entry =>
            entry.Path.EndsWith("packages.lock.json", StringComparison.OrdinalIgnoreCase));

        var buildTargets = Sorted(projects);
        var testProjects = Sorted(projects.Where(entry => IsTestProject(repository, entry)));

        // A solution is the repository's own statement of what belongs together, so it is
        // preferred over the individual projects it already names.
        return solutions.Count == 1
            ? new DotNetSurface(
                DotNetSurfaceKind.Present,
                solutions,
                testProjects.Count > 0 ? solutions : [],
                hasLockFiles,
                Reason: null)
            : new DotNetSurface(DotNetSurfaceKind.Present, buildTargets, testProjects, hasLockFiles, Reason: null);
    }

    /// <summary>A surface with no targets cannot be executed, so no execution detail applies.</summary>
    private static DotNetSurface Absent(string reason)
        => new(DotNetSurfaceKind.Absent, [], [], HasLockFiles: false, reason);

    private static DotNetSurface Ambiguous(string reason)
        => new(DotNetSurfaceKind.Ambiguous, [], [], HasLockFiles: false, reason);

    private static bool IsTestProject(GitRepository repository, TrackedEntry entry)
    {
        var (text, _) = repository.ReadTrackedText(entry);

        // Unreadable project text is not evidence of a test project. The gate that needs
        // the file will surface the same unreadable evidence when it runs.
        return text is not null
            && TestProjectMarkers.Any(marker => text.Contains(marker, StringComparison.OrdinalIgnoreCase));
    }

    private static bool HasExtension(string path, string[] extensions)
        => extensions.Any(extension => path.EndsWith(extension, StringComparison.OrdinalIgnoreCase));

    private static List<string> Sorted(IEnumerable<TrackedEntry> entries)
        => entries.Select(entry => entry.Path).OrderBy(path => path, StringComparer.Ordinal).ToList();
}

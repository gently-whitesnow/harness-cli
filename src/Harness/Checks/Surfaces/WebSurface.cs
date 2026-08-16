using System.Text.Json;
using Harness.Git;

namespace Harness.Checks.Surfaces;

internal enum WebSurfaceKind
{
    /// <summary>No tracked web evidence at all.</summary>
    Absent,

    /// <summary>There is web evidence, and it does not settle what the repository is.</summary>
    Ambiguous,

    /// <summary>Discovery read a definite web surface.</summary>
    Present,
}

/// <summary>
/// What Git says the repository's web side is: the manifest that represents it, the scripts
/// it declares and the packages it depends on. Nothing here is executed — the harness reads
/// this only to hold a declaration against what the repository visibly is.
/// </summary>
/// <param name="ManifestPath">Repository-relative path of the manifest that carries the scripts.</param>
/// <param name="Scripts">Declared script names and their commands.</param>
/// <param name="Dependencies">Every package name the manifest declares, in any dependency section.</param>
/// <param name="Reason">Why the surface is absent or ambiguous; absent when it is present.</param>
internal sealed record WebSurface(
    WebSurfaceKind Kind,
    string ManifestPath,
    IReadOnlyDictionary<string, string> Scripts,
    IReadOnlySet<string> Dependencies,
    string? Reason)
{
    private const string ManifestName = "package.json";

    public static WebSurface Discover(GitRepository repository)
    {
        var tracked = repository.TrackedEntries
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path))
            .ToList();

        var manifests = tracked
            .Where(entry => FileName(entry.Path) == ManifestName)
            .Select(entry => entry.Path)
            .OrderBy(path => path, StringComparer.Ordinal)
            .ToList();

        if (manifests.Count == 0)
        {
            return Absent($"no tracked {ManifestName}");
        }

        // A root manifest is the repository's own statement of what the project is; a
        // workspace member is not a second candidate for the same role.
        var manifestPath = manifests.Contains(ManifestName, StringComparer.Ordinal)
            ? ManifestName
            : manifests.Count == 1 ? manifests[0] : null;

        if (manifestPath is null)
        {
            return Ambiguous(
                $"{manifests.Count} tracked package manifests ({string.Join(", ", manifests)}) and no root "
                    + $"{ManifestName} do not single out which one represents the repository");
        }

        var (manifest, failure) = ReadManifest(repository, manifestPath);
        return manifest is null
            ? Ambiguous(failure!)
            : new WebSurface(
                WebSurfaceKind.Present,
                manifestPath,
                manifest.Scripts,
                manifest.Dependencies,
                Reason: null);
    }

    /// <summary>Which of the given script names the manifest declares, in the order asked.</summary>
    public IReadOnlyList<string> ScriptsAmong(IReadOnlyList<string> names)
        => names.Where(Scripts.ContainsKey).ToList();

    /// <summary>Which of the given package names the manifest depends on, in the order asked.</summary>
    public IReadOnlyList<string> DependenciesAmong(IReadOnlyList<string> names)
        => names.Where(Dependencies.Contains).ToList();

    private static WebSurface Absent(string reason) => Without(WebSurfaceKind.Absent, reason);

    private static WebSurface Ambiguous(string reason) => Without(WebSurfaceKind.Ambiguous, reason);

    private static WebSurface Without(WebSurfaceKind kind, string reason)
        => new(kind, "", new Dictionary<string, string>(), new HashSet<string>(StringComparer.Ordinal), reason);

    /// <summary>
    /// The manifest's scripts and dependency declarations. A manifest that cannot be parsed
    /// is reported rather than treated as a project with no scripts, which would silently
    /// look like a repository with no quality machinery.
    /// </summary>
    private static (ManifestContent? Manifest, string? Failure) ReadManifest(
        GitRepository repository,
        string manifestPath)
    {
        var entry = repository.TrackedEntries.First(candidate => candidate.Path == manifestPath);
        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure!);
        }

        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });

            return (
                new ManifestContent(ReadScripts(document.RootElement), ReadDependencies(document.RootElement)),
                null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{manifestPath}' is not readable as JSON ({exception.Message}), so what the "
                + "repository declares cannot be held against it");
        }
    }

    private static Dictionary<string, string> ReadScripts(JsonElement root)
    {
        var scripts = new Dictionary<string, string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("scripts", out var declared)
            || declared.ValueKind != JsonValueKind.Object)
        {
            return scripts;
        }

        foreach (var script in declared.EnumerateObject())
        {
            if (script.Value.ValueKind == JsonValueKind.String)
            {
                scripts[script.Name] = script.Value.GetString() ?? "";
            }
        }

        return scripts;
    }

    private static HashSet<string> ReadDependencies(JsonElement root)
    {
        var dependencies = new HashSet<string>(StringComparer.Ordinal);
        if (root.ValueKind != JsonValueKind.Object)
        {
            return dependencies;
        }

        foreach (var section in new[] { "dependencies", "devDependencies", "optionalDependencies" })
        {
            if (!root.TryGetProperty(section, out var declared) || declared.ValueKind != JsonValueKind.Object)
            {
                continue;
            }

            foreach (var package in declared.EnumerateObject())
            {
                dependencies.Add(package.Name);
            }
        }

        return dependencies;
    }

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    private sealed record ManifestContent(Dictionary<string, string> Scripts, HashSet<string> Dependencies);
}

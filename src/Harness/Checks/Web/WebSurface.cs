using System.Text.Json;
using Harness.Git;

namespace Harness.Checks.Web;

internal enum WebSurfaceKind
{
    /// <summary>No tracked web evidence at all; the web gates do not apply.</summary>
    Absent,

    /// <summary>There is web evidence, but no single defensible execution plan.</summary>
    Ambiguous,

    /// <summary>Discovery produced a definite plan the gates can execute.</summary>
    Present,
}

/// <summary>
/// What the web gates should run and how, derived only from Git-tracked evidence: the
/// package manifest, the lockfile that names the package manager, and the scripts the
/// repository already declares. Nothing here is configured, and nothing is taken from the
/// caller's environment: the same repository must be verified the same way by every
/// developer. When the evidence does not single out a plan, discovery says so.
/// </summary>
/// <param name="PackageManager">Executable named by the repository's lockfile evidence.</param>
/// <param name="ManifestPath">Repository-relative path of the manifest that carries the scripts.</param>
/// <param name="WorkingDirectory">Absolute directory the package manager is invoked from.</param>
/// <param name="Scripts">Declared script names and their commands.</param>
/// <param name="DeclaresDependencies">Whether the manifest declares dependencies that need installing.</param>
/// <param name="Reason">Why the surface is absent or ambiguous; absent when it is present.</param>
internal sealed record WebSurface(
    WebSurfaceKind Kind,
    string PackageManager,
    string ManifestPath,
    string WorkingDirectory,
    IReadOnlyDictionary<string, string> Scripts,
    bool DeclaresDependencies,
    string? Reason)
{
    private const string ManifestName = "package.json";

    /// <summary>
    /// Lockfiles the harness recognizes, and the executable each one is evidence for. A
    /// lockfile is the repository's own record of which package manager resolved its
    /// dependency graph, which is why it decides the plan.
    /// </summary>
    private static readonly (string LockFile, string PackageManager)[] LockFiles =
    [
        ("package-lock.json", "npm"),
        ("npm-shrinkwrap.json", "npm"),
        ("pnpm-lock.yaml", "pnpm"),
        ("yarn.lock", "yarn"),
        ("bun.lock", "bun"),
        ("bun.lockb", "bun"),
    ];

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
                    + $"{ManifestName} do not single out one execution plan; the web gates will not guess which "
                    + "one represents the repository.");
        }

        var directory = DirectoryOf(manifestPath);
        var lockFiles = LockFiles
            .Where(candidate => tracked.Any(entry => entry.Path == Join(directory, candidate.LockFile)))
            .ToList();

        var managers = lockFiles.Select(candidate => candidate.PackageManager).Distinct(StringComparer.Ordinal).ToList();
        if (managers.Count > 1)
        {
            return Ambiguous(
                $"conflicting package manager evidence next to {manifestPath}: "
                    + $"{string.Join(", ", lockFiles.Select(candidate => candidate.LockFile))}. One repository has "
                    + "one package manager, so the web gates will not choose between them.");
        }

        var (manifest, failure) = ReadManifest(repository, manifestPath);
        if (manifest is null)
        {
            return Ambiguous(failure!);
        }

        // `packageManager` is the manifest's own declaration, so it is evidence too. It must
        // agree with the lockfile; a repository that says two different things has not
        // singled out a plan any more than two lockfiles have.
        if (manifest.DeclaredPackageManager is { } declared)
        {
            if (!Supported(declared))
            {
                return Ambiguous(
                    $"{manifestPath} declares the package manager '{declared}', which this version of the harness "
                        + $"cannot run. Supported: {string.Join(", ", Supported())}.");
            }

            if (managers.Count == 1 && managers[0] != declared)
            {
                return Ambiguous(
                    $"conflicting package manager evidence: {manifestPath} declares '{declared}' while "
                        + $"{lockFiles[0].LockFile} is a '{managers[0]}' lockfile.");
            }

            managers = [declared];
        }

        if (managers.Count == 0)
        {
            return Ambiguous(
                $"{manifestPath} declares a web project, but no tracked lockfile or `packageManager` declaration "
                    + "names the package manager that resolved it. The web gates run the repository's own "
                    + "toolchain and will not guess which one that is.");
        }

        return new WebSurface(
            WebSurfaceKind.Present,
            managers[0],
            manifestPath,
            Path.Combine(repository.RootPath, directory),
            manifest.Scripts,
            manifest.DeclaresDependencies,
            Reason: null);
    }

    private static WebSurface Absent(string reason)
        => new(WebSurfaceKind.Absent, "", "", "", new Dictionary<string, string>(), false, reason);

    private static WebSurface Ambiguous(string reason)
        => new(WebSurfaceKind.Ambiguous, "", "", "", new Dictionary<string, string>(), false, reason);

    /// <summary>
    /// The manifest's scripts and dependency declarations. Only the two facts the gates act
    /// on are read, and a manifest that cannot be parsed is reported rather than treated as
    /// a project with no scripts, which would silently look like a repository with no
    /// quality commands.
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
                new ManifestContent(
                    ReadScripts(document.RootElement),
                    ReadDeclaresDependencies(document.RootElement),
                    DeclaredPackageManager(document.RootElement)),
                null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{manifestPath}' is not readable as JSON ({exception.Message}), so the web execution "
                + "plan cannot be determined.");
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

    /// <summary>The name part of a `packageManager` declaration such as "pnpm@9.1.0".</summary>
    private static string? DeclaredPackageManager(JsonElement root)
    {
        if (root.ValueKind != JsonValueKind.Object
            || !root.TryGetProperty("packageManager", out var declared)
            || declared.ValueKind != JsonValueKind.String)
        {
            return null;
        }

        var value = declared.GetString();
        if (string.IsNullOrWhiteSpace(value))
        {
            return null;
        }

        var separator = value.IndexOf('@', StringComparison.Ordinal);
        return separator < 0 ? value.Trim() : value[..separator].Trim();
    }

    private static IEnumerable<string> Supported()
        => LockFiles.Select(candidate => candidate.PackageManager).Distinct(StringComparer.Ordinal);

    private static bool Supported(string packageManager)
        => Supported().Contains(packageManager, StringComparer.Ordinal);

    private static bool ReadDeclaresDependencies(JsonElement root)
        => root.ValueKind == JsonValueKind.Object
            && new[] { "dependencies", "devDependencies", "optionalDependencies" }.Any(section =>
                root.TryGetProperty(section, out var declared)
                && declared.ValueKind == JsonValueKind.Object
                && declared.EnumerateObject().Any());

    private static string FileName(string path) => path[(path.LastIndexOf('/') + 1)..];

    /// <summary>The manifest's directory as a repository-relative path; empty at the root.</summary>
    private static string DirectoryOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? "" : path[..separator];
    }

    private static string Join(string directory, string name)
        => directory.Length == 0 ? name : directory + "/" + name;

    private sealed record ManifestContent(
        Dictionary<string, string> Scripts,
        bool DeclaresDependencies,
        string? DeclaredPackageManager);
}

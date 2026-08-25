using System.Xml.Linq;
using Harness.Git;

namespace Harness.Checks.DotNet;

internal static class DotNetRepository
{
    private static readonly string[] ProjectExtensions = [".csproj", ".fsproj", ".vbproj"];

    public static (IReadOnlyList<DotNetFile> Projects, string? Failure) ReadProjects(GitRepository repository)
    {
        var projects = new List<DotNetFile>();
        foreach (var entry in repository.TrackedEntries
            .Where(entry => ProjectExtensions.Contains(System.IO.Path.GetExtension(entry.Path), StringComparer.OrdinalIgnoreCase))
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path)))
        {
            var (file, failure) = ReadXml(repository, entry);
            if (failure is not null)
            {
                return ([], failure);
            }

            if (file!.Root.Attribute("Sdk") is not null || file.Root.Elements().Any(element => element.Name.LocalName == "Sdk"))
            {
                projects.Add(file);
            }
        }

        return (projects, null);
    }

    public static (DotNetFile? File, string? Failure) ReadNearest(
        GitRepository repository,
        string projectPath,
        string fileName)
    {
        var tracked = repository.TrackedEntries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        foreach (var candidate in Candidates(projectPath, fileName))
        {
            if (tracked.TryGetValue(candidate, out var entry))
            {
                return ReadXml(repository, entry);
            }
        }

        return (null, null);
    }

    /// <summary>
    /// Every path <see cref="ReadNearest"/> looks at, from the project's own directory up to
    /// the repository root. A finding about a missing file names them all, so the report can
    /// say which of them the author has already written without staging.
    /// </summary>
    public static IReadOnlyList<string> Candidates(string projectPath, string fileName)
    {
        var candidates = new List<string>();
        var directory = Directory(projectPath);
        while (true)
        {
            candidates.Add(directory.Length == 0 ? fileName : $"{directory}/{fileName}");
            if (directory.Length == 0)
            {
                return candidates;
            }

            directory = Directory(directory);
        }
    }

    public static IEnumerable<XElement> Elements(DotNetFile file, string localName)
        => file.Root.Descendants().Where(element => element.Name.LocalName == localName);

    public static string? Value(XElement element)
        => element.Attribute("Value")?.Value.Trim() is { Length: > 0 } attribute
            ? attribute
            : element.Value.Trim() is { Length: > 0 } content ? content : null;

    public static string NormalizeRelative(string baseFile, string relativePath)
    {
        var baseDirectory = Directory(baseFile);
        var combined = baseDirectory.Length == 0 ? relativePath : $"{baseDirectory}/{relativePath}";
        var segments = new List<string>();
        foreach (var segment in combined.Replace('\\', '/').Split('/', StringSplitOptions.RemoveEmptyEntries))
        {
            if (segment == ".")
            {
                continue;
            }

            if (segment == ".." && segments.Count > 0)
            {
                segments.RemoveAt(segments.Count - 1);
                continue;
            }

            segments.Add(segment);
        }

        return string.Join('/', segments);
    }

    private static (DotNetFile? File, string? Failure) ReadXml(GitRepository repository, TrackedEntry entry)
    {
        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure);
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.None);
            return document.Root is null
                ? (null, $"'{entry.Path}' has no XML root element")
                : (new DotNetFile(entry.Path, document.Root), null);
        }
        catch (Exception exception) when (exception is System.Xml.XmlException or InvalidOperationException)
        {
            return (null, $"'{entry.Path}' is not readable XML ({exception.Message})");
        }
    }

    private static string Directory(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}

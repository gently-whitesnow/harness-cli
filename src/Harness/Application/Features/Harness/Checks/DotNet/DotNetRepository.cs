using System.Xml.Linq;
using Harness.Repository;

namespace Harness.Checks.DotNet;

internal static class DotNetRepository
{
    /// <summary>
    /// The project files themselves. Without a tracked one there is nothing to judge, so every
    /// .NET policy reads them and names them as the evidence it needs.
    /// </summary>
    public static readonly IReadOnlyList<EvidenceFile> ProjectFiles =
        [new("*.csproj"), new("*.fsproj"), new("*.vbproj")];

    public static (IReadOnlyList<DotNetFile> Projects, string? Failure) ReadProjects(CheckContext context)
    {
        var projects = new List<DotNetFile>();
        foreach (var entry in ProjectFiles
            .SelectMany(context.Tracked)
            .Where(entry => !RepositoryLocations.IsGenerated(entry.Path)))
        {
            var (file, failure) = ReadXml(context.Repository, entry);
            if (failure is not null)
            {
                return ([], failure);
            }

            if (IsSdkStyle(file!))
            {
                projects.Add(file!);
            }
        }

        return (projects, null);
    }

    /// <summary>Reads one tracked project or props file as XML for a check that found it itself.</summary>
    public static (DotNetFile? File, string? Failure) Read(IRepository repository, TrackedEntry entry)
        => ReadXml(repository, entry);

    /// <summary>The SDK judgement shared by every reader of tracked project XML.</summary>
    public static bool IsSdkStyle(DotNetFile file)
        => file.Root.Attribute("Sdk") is not null
            || file.Root.Elements().Any(element => element.Name.LocalName == "Sdk");

    public static (DotNetFile? File, string? Failure) ReadNearest(
        CheckContext context,
        string projectPath,
        EvidenceFile file)
    {
        var entry = context.Nearest(file, projectPath);
        return entry is null ? (null, null) : ReadXml(context.Repository, entry);
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

    private static (DotNetFile? File, string? Failure) ReadXml(IRepository repository, TrackedEntry entry)
    {
        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure);
        }

        try
        {
            var document = XDocument.Parse(text, LoadOptions.SetLineInfo);
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

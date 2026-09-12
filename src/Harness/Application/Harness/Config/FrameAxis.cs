using Harness.Languages;
using Harness.Languages.Ansible;
using Harness.Repository;

namespace Harness.Config;

/// <summary>
/// One applicability axis of the frame and the tracked files that make it relevant: a language
/// by its suffixes, .NET by project files, Ansible by markers among its YAML. `init`, `upgrade`
/// and `harness.coverage` detect an axis the same way, so none of them can forget a stack.
/// </summary>
internal sealed record FrameAxis(
    string Key,
    string Name,
    IReadOnlyList<EvidenceFile> Sources,
    Func<IRepository, IEnumerable<TrackedEntry>, List<string>>? Markers = null)
{
    public static readonly FrameAxis DotNet = new("dotnet", ".NET", [new("*.csproj"), new("*.fsproj"), new("*.vbproj")]);

    public static readonly FrameAxis Ansible = new(Language.Ansible.Key, Language.Ansible.Name, AnsibleMarkers.Sources, AnsibleMarkers.Detect);

    /// <summary>Every axis the harness ships, in the order the frame lists them.</summary>
    public static readonly IReadOnlyList<FrameAxis> All =
    [
        .. Language.All.Where(language => language.Suffixes.Count > 0).Select(Of),
        Ansible,
        DotNet,
    ];

    public static FrameAxis Of(Language language)
        => new(language.Key, language.Name, language.Suffixes.Select(suffix => new EvidenceFile($"*{suffix}")).ToList());

    public static FrameAxis? Named(string key)
        => All.FirstOrDefault(axis => string.Equals(axis.Key, key, StringComparison.Ordinal));

    /// <summary>The sources of this axis among the entries, outside generated locations; a marker axis keeps the markers.</summary>
    public List<string> Detect(IRepository repository, IEnumerable<TrackedEntry> entries)
    {
        var sources = entries
            .Where(entry => !entry.IsSymbolicLink && !RepositoryLocations.IsGenerated(entry.Path))
            .Where(entry => Sources.Any(source => source.Matches(entry.Path)))
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        return Markers is null
            ? sources.Select(entry => entry.Path).Order(StringComparer.Ordinal).ToList()
            : Markers(repository, sources);
    }

    /// <summary>The axes with at least one tracked source, in frame order.</summary>
    public static List<FrameAxis> Detected(IRepository repository)
        => All.Where(axis => axis.Detect(repository, repository.TrackedEntries).Count > 0).ToList();
}

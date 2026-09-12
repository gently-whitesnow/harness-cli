using Harness.Checks;
using Harness.Languages;
using Harness.Repository;

namespace Harness.Config;

/// <summary>
/// One applicability axis of the frame and the tracked files that make it relevant to a
/// repository: a language by its source suffixes, or the .NET axis by its project files.
/// `init`, `upgrade` and `harness.coverage` all detect an axis the same way, so a language
/// the index shows cannot be forgotten by one of them and remembered by another.
/// </summary>
internal sealed record FrameAxis(string Key, string Name, IReadOnlyList<EvidenceFile> Sources)
{
    public static readonly FrameAxis DotNet = new("dotnet", ".NET", DotNetRepository.ProjectFiles);

    /// <summary>Every axis the harness ships, in the order the frame lists them.</summary>
    public static readonly IReadOnlyList<FrameAxis> All =
    [
        .. Language.All.Select(Of),
        DotNet,
    ];

    public static FrameAxis Of(Language language)
        => new(language.Key, language.Name, language.Suffixes.Select(suffix => new EvidenceFile($"*{suffix}")).ToList());

    public static FrameAxis? Named(string key)
        => All.FirstOrDefault(axis => string.Equals(axis.Key, key, StringComparison.Ordinal));

    /// <summary>Tracked files of this axis outside generated, vendored and build-output locations.</summary>
    public List<string> Detect(IEnumerable<TrackedEntry> entries)
        => entries
            .Where(entry => !entry.IsSymbolicLink && !RepositoryLocations.IsGenerated(entry.Path))
            .Where(entry => Sources.Any(source => source.Matches(entry.Path)))
            .Select(entry => entry.Path)
            .Order(StringComparer.Ordinal)
            .ToList();

    /// <summary>The axes with at least one tracked source, in frame order.</summary>
    public static List<FrameAxis> Detected(IReadOnlyList<TrackedEntry> entries)
        => All.Where(axis => axis.Detect(entries).Count > 0).ToList();
}

using Harness.Config;
using Harness.Repository;

namespace Harness.Checks;

/// <summary>
/// What one check may read, and the only way it reads a tracked file by name. Undeclared
/// evidence is refused, so a check cannot depend on a file the report cannot explain.
/// </summary>
internal sealed class CheckContext(
    IRepository repository,
    HarnessConfig? config,
    string? configFailure,
    string checkId,
    IReadOnlyList<EvidenceFile> declaredEvidence)
{
    public IRepository Repository { get; } = repository;

    public HarnessConfig? Config { get; } = config;

    public string? ConfigFailure { get; } = configFailure;

    public IReadOnlyList<TrackedEntry> Tracked(EvidenceFile file)
    {
        RequireDeclared(file);
        return Repository.TrackedEntries.Where(entry => file.Matches(entry.Path)).ToList();
    }

    /// <summary>The declared evidence in the directory of <paramref name="startPath"/>, else above it.</summary>
    public TrackedEntry? Nearest(EvidenceFile file, string startPath)
    {
        if (file.IsPattern)
        {
            throw new InvalidOperationException(
                $"{checkId} asks for the nearest '{file.Name}', which names a shape rather than a file.");
        }

        var candidates = Tracked(file).ToDictionary(entry => entry.Path, StringComparer.Ordinal);
        var directory = DirectoryOf(startPath);
        while (true)
        {
            var candidate = directory.Length == 0 ? file.Name : $"{directory}/{file.Name}";
            if (candidates.TryGetValue(candidate, out var entry))
            {
                return entry;
            }

            if (directory.Length == 0)
            {
                return null;
            }

            directory = DirectoryOf(directory);
        }
    }

    private void RequireDeclared(EvidenceFile file)
    {
        if (!declaredEvidence.Contains(file))
        {
            throw new InvalidOperationException(
                $"{checkId} reads '{file.Name}' without naming it in Evidence, so a run could not say when "
                    + "that file is present but untracked.");
        }
    }

    private static string DirectoryOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..separator];
    }
}

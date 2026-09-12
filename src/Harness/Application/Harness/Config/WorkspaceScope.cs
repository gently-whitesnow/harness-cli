using Harness.Repository;

namespace Harness.Config;

/// <summary>
/// What the root of a workspace keeps to itself and how the index divides between frames
/// (ADR-0060). `check` and `upgrade` both read the split from here, so a root-only check is
/// named once.
/// </summary>
internal static class WorkspaceScope
{
    /// <summary>The commit integration is installed per clone, so its check belongs to the root alone.</summary>
    public const string RootOnlyCheck = "commits.setup";

    public const string RootOnlyGroup = "commits";

    public static bool IsRootOnlySelector(string selector) => selector is RootOnlyCheck or RootOnlyGroup;

    public static bool IsProjectCheck(string checkId) => checkId != RootOnlyCheck;

    /// <summary>The root frame's inventory: everything outside the registered projects.</summary>
    public static IRepository RootScope(IRepository repository, IReadOnlyList<string> projects)
        => projects.Count == 0 ? repository : new ScopedRepository(repository, string.Empty, projects);

    public static string ConfigPath(string project) => project + "/" + HarnessConfig.FileName;

    /// <summary>Tracked nested configs no root `projects` entry names, in index order.</summary>
    public static List<string> Unregistered(IRepository repository, IReadOnlyList<string> projects)
    {
        var registered = projects.Select(ConfigPath).ToHashSet(StringComparer.Ordinal);
        return repository.TrackedEntries
            .Select(entry => entry.Path)
            .Where(path => path.EndsWith("/" + HarnessConfig.FileName, StringComparison.Ordinal) && !registered.Contains(path))
            .ToList();
    }
}

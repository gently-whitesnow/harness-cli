namespace Harness.Git;

/// <summary>
/// Where tracked content describes someone else's code or a build product rather than the
/// repository itself. Checks share this judgement so a dependency cache or a committed
/// build output cannot create findings or a fabricated execution plan.
/// </summary>
internal static class RepositoryLocations
{
    private static readonly HashSet<string> GeneratedDirectories = new(StringComparer.Ordinal)
    {
        "node_modules",
        "vendor",
        "third_party",
        "bin",
        "obj",
        "dist",
        "build",
        "out",
        "target",
        "coverage",
        ".venv",
        "site-packages",
    };

    public static bool IsGenerated(string path)
        => path.Split('/').Any(segment => GeneratedDirectories.Contains(segment));
}

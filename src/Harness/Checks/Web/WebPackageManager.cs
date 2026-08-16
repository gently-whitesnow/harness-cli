using Harness.Processes;

namespace Harness.Checks.Web;

/// <summary>
/// Whether the package manager the repository asked for can be used at all. The harness
/// never installs one and never substitutes another: a package manager it cannot invoke
/// makes verification incomplete, never a repository violation.
/// </summary>
internal static class WebPackageManager
{
    /// <summary>
    /// Node tooling localizes its output, decorates it with colour and, in an interactive
    /// terminal, waits for input. The gates read that output as evidence and can never
    /// answer a prompt, so language is pinned, colour is off and the non-interactive mode
    /// every runner honours is requested.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> InvariantOutput =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["LANG"] = "en_US.UTF-8",
            ["LC_ALL"] = "en_US.UTF-8",
            ["CI"] = "1",
            ["NO_COLOR"] = "1",
            ["FORCE_COLOR"] = "0",
        };

    /// <summary>
    /// Every web gate needs the same answer and the probe costs a process. The directory is
    /// part of the key because a version manager can select a different executable per
    /// directory, so the cache can never answer for a directory it was not asked about.
    /// </summary>
    private static readonly Dictionary<(string Executable, string Directory), string?> Probed = [];

    /// <summary>
    /// Whether the dependencies the manifest declares are present to run against. This is
    /// working-tree state rather than repository evidence: it says what can be verified
    /// here and now, not what the repository is.
    /// </summary>
    public static bool DependenciesInstalled(WebSurface surface)
        => Directory.Exists(Path.Combine(surface.WorkingDirectory, "node_modules"));

    /// <summary>Runs one of the repository's scripts; never an install and never a fix.</summary>
    public static ProcessResult RunScript(WebSurface surface, string script)
        => ProcessRunner.Run(
            surface.PackageManager, ["run", script], surface.WorkingDirectory, InvariantOutput);

    /// <summary>Returns why the package manager is unusable, or null when it is usable.</summary>
    public static string? Verify(WebSurface surface)
    {
        var key = (surface.PackageManager, surface.WorkingDirectory);
        if (Probed.TryGetValue(key, out var cached))
        {
            return cached;
        }

        var run = ProcessRunner.Run(
            surface.PackageManager, ["--version"], surface.WorkingDirectory, InvariantOutput);

        var failure = run switch
        {
            { Failure: not null } =>
                $"`{surface.ManifestPath}` requires the {surface.PackageManager} package manager: {run.Failure}",
            { ExitCode: not 0 } =>
                $"`{surface.ManifestPath}` requires the {surface.PackageManager} package manager, but "
                    + $"`{surface.PackageManager} --version` exited with {run.ExitCode}. The harness does not "
                    + "install a toolchain.",
            _ => null,
        };

        Probed[key] = failure;
        return failure;
    }
}

using System.Diagnostics;
using Harness.Versioning;

namespace Harness.Git;

/// <summary>
/// The harness the managed hook will run, resolved the way the hook script resolves it:
/// the clone-local binary first, then `harness` on PATH. Nothing here depends on the
/// process that happens to be asking, so every caller in a clone sees the same answer.
/// </summary>
internal sealed record HookBinary(string? Path, string? CloneLocalPath)
{
    public const string CloneDirectory = "harness";

    private const int VersionTimeoutMilliseconds = 5000;

    public static HookBinary Resolve(string gitCommonDirectory)
    {
        var cloneLocal = System.IO.Path.Combine(gitCommonDirectory, CloneDirectory, "bin", FileName());
        return new HookBinary(IsExecutable(cloneLocal) ? cloneLocal : OnPath(), cloneLocal);
    }

    /// <summary>Asks the resolved binary which release it is, so a stale install is named, not guessed.</summary>
    public (HarnessVersion? Version, string? Failure) Release()
    {
        if (Path is null)
        {
            return (null, "no harness binary is resolvable");
        }

        var startInfo = new ProcessStartInfo(Path)
        {
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        startInfo.ArgumentList.Add("version");

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return (null, $"'{Path}' could not be started");
            }

            var output = process.StandardOutput.ReadToEndAsync();
            process.StandardError.ReadToEndAsync();
            if (!process.WaitForExit(VersionTimeoutMilliseconds))
            {
                process.Kill(entireProcessTree: true);
                return (null, $"'{Path} version' did not answer within {VersionTimeoutMilliseconds / 1000} seconds");
            }

            var first = output.Result.Split('\n')[0].Trim();
            var release = first.StartsWith("harness ", StringComparison.Ordinal)
                ? first["harness ".Length..].Trim()
                : first;
            return HarnessVersion.TryParse(release, out var version)
                ? (version, null)
                : (null, $"'{Path} version' printed '{first}' instead of a release");
        }
        catch (Exception exception) when (exception is IOException or SystemException)
        {
            return (null, $"'{Path} version' failed: {exception.Message}");
        }
    }

    private static string FileName() => OperatingSystem.IsWindows() ? "harness.exe" : "harness";

    private static string? OnPath()
    {
        var searchPath = Environment.GetEnvironmentVariable("PATH");
        if (string.IsNullOrEmpty(searchPath))
        {
            return null;
        }

        foreach (var directory in searchPath.Split(System.IO.Path.PathSeparator))
        {
            if (directory.Length == 0)
            {
                continue;
            }

            var candidate = System.IO.Path.Combine(directory, FileName());
            if (IsExecutable(candidate))
            {
                return System.IO.Path.GetFullPath(candidate);
            }
        }

        return null;
    }

    private static bool IsExecutable(string path)
    {
        if (!File.Exists(path))
        {
            return false;
        }

        return OperatingSystem.IsWindows()
            || (File.GetUnixFileMode(path) & UnixFileMode.UserExecute) != 0;
    }
}

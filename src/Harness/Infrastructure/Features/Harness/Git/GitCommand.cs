using System.Diagnostics;

namespace Harness.Git;

/// <summary>Runs Git directly, without a shell, and captures its output and launch failures.</summary>
internal static class GitCommand
{
    public static GitCommandResult Run(IReadOnlyList<string> arguments, string workingDirectory)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return Failed("Could not start Git.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stopwatch.Stop();
            return new GitCommandResult(
                process.ExitCode,
                standardOutput.Result,
                standardError.Result,
                stopwatch.Elapsed,
                Failure: null);
        }
        catch (Exception exception)
        {
            return Failed($"Could not run Git: {exception.Message}");
        }

        GitCommandResult Failed(string failure)
        {
            stopwatch.Stop();
            return new GitCommandResult(-1, "", "", stopwatch.Elapsed, failure);
        }
    }
}

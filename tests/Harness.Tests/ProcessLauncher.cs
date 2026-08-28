using System.Diagnostics;

namespace Harness.Tests;

/// <summary>
/// Starts a process, captures both streams and waits for it. Every test that reaches
/// outside its own process — the harness, Git, the SDK — goes through here.
/// </summary>
public static class ProcessLauncher
{
    private static readonly TimeSpan Timeout = TimeSpan.FromMinutes(10);

    public static CliRun Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null,
        IReadOnlyList<string>? removeFromEnvironment = null,
        string? standardInput = null)
    {
        var startInfo = new ProcessStartInfo(executable)
        {
            WorkingDirectory = workingDirectory,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            RedirectStandardInput = true,
            UseShellExecute = false,
        };

        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        // Keep runs reproducible regardless of the developer's Git configuration.
        startInfo.Environment["GIT_CONFIG_GLOBAL"] = "/dev/null";
        startInfo.Environment["GIT_CONFIG_SYSTEM"] = "/dev/null";

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        foreach (var name in removeFromEnvironment ?? [])
        {
            startInfo.Environment.Remove(name);
        }

        using var process = Process.Start(startInfo)
            ?? throw new InvalidOperationException($"Could not start {executable}.");

        if (standardInput is not null)
        {
            process.StandardInput.Write(standardInput);
        }

        process.StandardInput.Close();

        var standardOutput = process.StandardOutput.ReadToEndAsync();
        var standardError = process.StandardError.ReadToEndAsync();

        if (!process.WaitForExit((int)Timeout.TotalMilliseconds))
        {
            process.Kill(entireProcessTree: true);
            throw new TimeoutException($"{executable} did not exit within {Timeout}.");
        }

        return new CliRun(process.ExitCode, standardOutput.Result, standardError.Result);
    }
}

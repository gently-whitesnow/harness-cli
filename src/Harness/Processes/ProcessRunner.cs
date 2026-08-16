using System.Diagnostics;

namespace Harness.Processes;

/// <summary>Result of one external command invocation.</summary>
/// <param name="DisplayCommand">The command as it can be reproduced outside the harness.</param>
/// <param name="StandardError">Diagnostic output, bounded so a runaway command cannot flood a report.</param>
/// <param name="Failure">Set when the command could not be run at all.</param>
internal sealed record ProcessResult(
    string DisplayCommand,
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string? Failure);

/// <summary>
/// Invokes executables with an argument vector — never through a shell — and reports
/// the command as displayed, its exit status, its output and how long it took.
/// </summary>
internal static class ProcessRunner
{
    /// <summary>Diagnostic output beyond this is not useful in a report and is discarded.</summary>
    private const int DiagnosticLimit = 4000;

    /// <param name="environment">
    /// Variables set for the child only. Callers that read a tool's output use this to pin
    /// what the tool emits, so evidence does not depend on the developer's locale.
    /// </param>
    public static ProcessResult Run(
        string executable,
        IReadOnlyList<string> arguments,
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment = null)
    {
        var display = executable + " " + string.Join(' ', arguments);
        var startInfo = new ProcessStartInfo(executable)
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

        foreach (var (name, value) in environment ?? new Dictionary<string, string>())
        {
            startInfo.Environment[name] = value;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new ProcessResult(display, -1, "", "", stopwatch.Elapsed, $"Could not start '{display}'.");
            }

            var standardOutput = process.StandardOutput.ReadToEndAsync();
            var standardError = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            stopwatch.Stop();

            return new ProcessResult(
                display,
                process.ExitCode,
                standardOutput.Result,
                Bound(standardError.Result),
                stopwatch.Elapsed,
                Failure: null);
        }
        catch (Exception exception)
        {
            stopwatch.Stop();
            return new ProcessResult(
                display, -1, "", "", stopwatch.Elapsed, $"Could not run '{display}': {exception.Message}");
        }
    }

    private static string Bound(string diagnostics)
        => diagnostics.Length <= DiagnosticLimit
            ? diagnostics
            : diagnostics[..DiagnosticLimit] + "… (output truncated)";
}

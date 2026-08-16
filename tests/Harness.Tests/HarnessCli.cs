using System.Reflection;

namespace Harness.Tests;

/// <summary>
/// Runs the compiled harness executable — the only seam these tests observe.
/// </summary>
public static class HarnessCli
{
    private static readonly string ExecutablePath = ResolveExecutablePath();

    public static CliRun Run(string workingDirectory, params string[] arguments)
        => Run(workingDirectory, environment: null, arguments);

    /// <summary>
    /// Runs the executable with environment overrides, so environmental failures can be
    /// proven through the real process boundary instead of internal mocks.
    /// </summary>
    public static CliRun Run(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
        => ProcessLauncher.Run(ExecutablePath, arguments, workingDirectory, environment);

    /// <summary>The check identifiers this build ships, as the CLI itself reports them.</summary>
    public static IReadOnlyList<string> ShippedCheckIds(string workingDirectory)
    {
        var help = Run(workingDirectory, "help");
        var lines = help.Output.Split('\n');
        var checksHeading = Array.FindIndex(lines, line => line.StartsWith("Checks", StringComparison.Ordinal));
        Assert.True(checksHeading >= 0, "`harness help` does not document the shipped checks:\n" + help.Output);

        return lines
            .Skip(checksHeading + 1)
            .TakeWhile(line => line.StartsWith("  ", StringComparison.Ordinal))
            .Select(line => line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries)[0])
            .ToList();
    }

    /// <summary>
    /// The most lines a default run may print: fixed framing plus a bounded cost per shipped
    /// gate. Concise output is a contract about density — it grows with the number of checks
    /// the build ships and never with the number of documents or findings they report on.
    /// </summary>
    public static int ConciseLineBudget(string workingDirectory)
        => 4 + (2 * ShippedCheckIds(workingDirectory).Count);

    private static string ResolveExecutablePath()
    {
        var overridePath = Environment.GetEnvironmentVariable("HARNESS_CLI");
        if (!string.IsNullOrWhiteSpace(overridePath))
        {
            return overridePath;
        }

        var configured = typeof(HarnessCli).Assembly
            .GetCustomAttributes<AssemblyMetadataAttribute>()
            .FirstOrDefault(attribute => attribute.Key == "HarnessCliPath")
            ?.Value;

        if (string.IsNullOrWhiteSpace(configured) || !File.Exists(configured))
        {
            throw new InvalidOperationException(
                $"The harness executable was not found at '{configured}'. Build src/Harness first.");
        }

        return configured;
    }
}

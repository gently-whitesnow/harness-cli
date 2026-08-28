using System.Reflection;

namespace Harness.Tests;

public static class HarnessCli
{
    private static readonly string ExecutablePath = ResolveExecutablePath();

    public static CliRun Run(string workingDirectory, params string[] arguments)
        => Run(workingDirectory, environment: null, arguments);

    public static CliRun RunWithInput(string workingDirectory, string input, params string[] arguments)
        => ProcessLauncher.Run(ExecutablePath, arguments, workingDirectory, standardInput: input);

    public static CliRun RunVerbose(string workingDirectory, params string[] arguments)
    {
        var verboseArguments = arguments.Concat(["--verbose"]).ToArray();
        return Run(workingDirectory, environment: null, verboseArguments);
    }

    public static CliRun Run(
        string workingDirectory,
        IReadOnlyDictionary<string, string>? environment,
        params string[] arguments)
        => ProcessLauncher.Run(ExecutablePath, arguments, workingDirectory, environment);

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

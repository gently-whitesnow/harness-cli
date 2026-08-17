namespace Harness.Tests;

/// <summary>Result of one process the tests started.</summary>
public sealed record CliRun(int ExitCode, string StandardOutput, string StandardError)
{
    public string Output => StandardOutput + StandardError;

    public bool OutputContains(string value)
        => Output.Contains(value, StringComparison.Ordinal);
}

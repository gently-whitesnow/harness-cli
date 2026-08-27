namespace Harness.Git;

internal sealed record GitCommandResult(
    int ExitCode,
    string StandardOutput,
    string StandardError,
    TimeSpan Duration,
    string? Failure);

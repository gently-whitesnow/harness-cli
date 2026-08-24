namespace Harness.Commits;

internal sealed record CommitMessageReport(
    IReadOnlyList<string> Errors,
    IReadOnlyList<string> Warnings)
{
    public bool Passed => Errors.Count == 0;
}

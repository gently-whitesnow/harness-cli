namespace Harness.Repository;

internal interface IRepository
{
    string RootPath { get; }

    IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    TimeSpan ReadDuration { get; }

    (IReadOnlyList<(string ObjectId, string Message)>? Commits, string? Failure) ReadCommits(
        string revisionRange);

    (IReadOnlyList<string>? Paths, string? Failure) ReadUntrackedPaths();

    (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry);

    (string? Text, string? Failure) ReadTrackedText(TrackedEntry entry);
}

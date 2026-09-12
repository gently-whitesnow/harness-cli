namespace Harness.Repository;

internal interface IRepository
{
    string RootPath { get; }

    IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    TimeSpan ReadDuration { get; }

    /// <summary>
    /// The tracked copies of one file name in the directories above this repository's root,
    /// nearest first, as `../`-prefixed entries. A whole repository has nothing above it; a
    /// project scope answers with the shared configuration its toolchain resolves by walking up.
    /// </summary>
    IReadOnlyList<TrackedEntry> Ancestors(string fileName) => [];

    (IReadOnlyList<(string ObjectId, string Message)>? Commits, string? Failure) ReadCommits(
        string revisionRange);

    (IReadOnlyList<string>? Paths, string? Failure) ReadUntrackedPaths();

    (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry);

    (string? Text, string? Failure) ReadTrackedText(TrackedEntry entry);
}

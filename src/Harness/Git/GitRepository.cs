namespace Harness.Git;

/// <summary>
/// Read access to Git evidence. Failures are returned so callers can report incomplete
/// checks instead of guessing a verdict.
/// </summary>
internal sealed class GitRepository
{
    private readonly Dictionary<string, string> blobs = new(StringComparer.Ordinal);

    private IReadOnlyList<string>? untracked;

    private GitRepository(string rootPath, IReadOnlyList<TrackedEntry> trackedEntries, TimeSpan readDuration)
    {
        RootPath = rootPath;
        TrackedEntries = trackedEntries;
        ReadDuration = readDuration;
    }

    public string RootPath { get; }

    public IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    public (IReadOnlyList<(string ObjectId, string Message)>? Commits, string? Failure) ReadCommits(
        string revisionRange)
    {
        if (string.IsNullOrWhiteSpace(revisionRange) || revisionRange.StartsWith('-'))
        {
            return (null, "The commit range must be a non-empty Git revision and must not start with '-'.");
        }

        var revisions = RunGit(["rev-list", "--reverse", revisionRange], RootPath);
        if (revisions.Failure is not null)
        {
            return (null, revisions.Failure);
        }

        if (revisions.ExitCode != 0)
        {
            return (null, $"Could not resolve commit range '{revisionRange}' ({Summarize(revisions.StandardError)}).");
        }

        var commits = new List<(string ObjectId, string Message)>();
        foreach (var objectId in revisions.StandardOutput.Split('\n', StringSplitOptions.RemoveEmptyEntries))
        {
            var message = RunGit(["show", "-s", "--format=%B", objectId], RootPath);
            if (message.Failure is not null)
            {
                return (null, message.Failure);
            }

            if (message.ExitCode != 0)
            {
                return (null, $"Could not read commit '{objectId}' ({Summarize(message.StandardError)}).");
            }

            commits.Add((objectId, message.StandardOutput));
        }

        return (commits, null);
    }

    public TimeSpan ReadDuration { get; }

    public static (GitRepository? Repository, string? Failure) Open(string path)
    {
        if (!Directory.Exists(path))
        {
            return (null, $"Path '{path}' does not exist or is not a directory.");
        }

        var topLevel = RunGit(["rev-parse", "--show-toplevel"], path);
        if (topLevel.Failure is not null)
        {
            return (null, topLevel.Failure);
        }

        if (topLevel.ExitCode != 0)
        {
            return (null, $"'{path}' is not inside a Git repository ({Summarize(topLevel.StandardError)}).");
        }

        var rootPath = topLevel.StandardOutput.Trim();
        if (rootPath.Length == 0)
        {
            return (null, $"Git did not report a repository root for '{path}'.");
        }

        var listing = RunGit(["ls-files", "--stage", "-z"], rootPath);
        if (listing.Failure is not null)
        {
            return (null, listing.Failure);
        }

        if (listing.ExitCode != 0)
        {
            return (null, $"Could not read the Git index of '{rootPath}' ({Summarize(listing.StandardError)}).");
        }

        var (entries, parseFailure) = ParseIndex(listing.StandardOutput);
        return parseFailure is not null
            ? (null, parseFailure)
            : (new GitRepository(rootPath, entries, topLevel.Duration + listing.Duration), null);
    }

    /// <summary>
    /// Reads untracked, non-ignored paths on demand; they explain findings but never produce
    /// one, so successful runs do not pay for this query.
    /// </summary>
    public (IReadOnlyList<string>? Paths, string? Failure) ReadUntrackedPaths()
    {
        if (untracked is not null)
        {
            return (untracked, null);
        }

        var listing = RunGit(["ls-files", "--others", "--exclude-standard", "-z"], RootPath);
        if (listing.Failure is not null)
        {
            return (null, listing.Failure);
        }

        if (listing.ExitCode != 0)
        {
            return (null, $"Could not list the untracked files of '{RootPath}' ({Summarize(listing.StandardError)}).");
        }

        untracked = listing.StandardOutput.Split('\0', StringSplitOptions.RemoveEmptyEntries);
        return (untracked, null);
    }

    public (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry)
    {
        var (text, failure) = ReadBlob(entry);
        return (text?.Trim(), failure);
    }

    /// <summary>
    /// Prefers working-tree text, but falls back to the staged blob when the tracked path is
    /// absent, preserving evidence for a pending deletion.
    /// </summary>
    public (string? Text, string? Failure) ReadTrackedText(TrackedEntry entry)
    {
        var absolutePath = System.IO.Path.Combine(RootPath, entry.Path);
        if (File.Exists(absolutePath))
        {
            try
            {
                return (File.ReadAllText(absolutePath), null);
            }
            catch (Exception exception)
            {
                return (null, $"Could not read '{entry.Path}': {exception.Message}");
            }
        }

        return ReadBlob(entry);
    }

    private (string? Text, string? Failure) ReadBlob(TrackedEntry entry)
    {
        if (blobs.TryGetValue(entry.ObjectId, out var cached))
        {
            return (cached, null);
        }

        var blob = RunGit(["cat-file", "blob", entry.ObjectId], RootPath);
        if (blob.Failure is not null)
        {
            return (null, blob.Failure);
        }

        if (blob.ExitCode != 0)
        {
            return (null, $"Could not read the staged content of '{entry.Path}' ({Summarize(blob.StandardError)}).");
        }

        blobs[entry.ObjectId] = blob.StandardOutput;
        return (blob.StandardOutput, null);
    }

    private static (List<TrackedEntry> Entries, string? Failure) ParseIndex(string output)
    {
        var entries = new List<TrackedEntry>();
        foreach (var record in output.Split('\0', StringSplitOptions.RemoveEmptyEntries))
        {
            var tab = record.IndexOf('\t');
            if (tab < 0)
            {
                return (entries, $"Unexpected Git index record: '{record}'.");
            }

            var fields = record[..tab].Split(' ', StringSplitOptions.RemoveEmptyEntries);
            if (fields.Length < 2)
            {
                return (entries, $"Unexpected Git index record: '{record}'.");
            }

            entries.Add(new TrackedEntry(record[(tab + 1)..], fields[0], fields[1]));
        }

        return (entries, null);
    }

    private static GitCommandResult RunGit(IReadOnlyList<string> arguments, string workingDirectory)
        => GitCommand.Run(arguments, workingDirectory);

    private static string Summarize(string standardError)
    {
        var text = standardError.Trim();
        if (text.Length == 0)
        {
            return "no diagnostic output";
        }

        var firstLine = text.Split('\n')[0].Trim();
        return firstLine.Length > 200 ? firstLine[..200] : firstLine;
    }
}

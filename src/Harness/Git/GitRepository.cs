using Harness.Processes;

namespace Harness.Git;

/// <summary>One entry of the Git index.</summary>
/// <param name="Path">Repository-relative path, always with '/' separators.</param>
/// <param name="Mode">Git file mode, for example 100644 for a regular file and 120000 for a symbolic link.</param>
/// <param name="ObjectId">Blob identifier of the staged content.</param>
internal sealed record TrackedEntry(string Path, string Mode, string ObjectId)
{
    public bool IsSymbolicLink => Mode == "120000";
}

/// <summary>
/// Read access to the Git evidence the harness relies on: which paths are tracked, what
/// Git stores for them, and their text. Every failure to obtain evidence is reported as a
/// reason rather than guessed at, so callers can end a check as incomplete instead of
/// inventing a pass or a violation.
/// </summary>
internal sealed class GitRepository
{
    private readonly Dictionary<string, string> blobs = new(StringComparer.Ordinal);

    private GitRepository(string rootPath, IReadOnlyList<TrackedEntry> trackedEntries, TimeSpan readDuration)
    {
        RootPath = rootPath;
        TrackedEntries = trackedEntries;
        ReadDuration = readDuration;
    }

    public string RootPath { get; }

    public IReadOnlyList<TrackedEntry> TrackedEntries { get; }

    /// <summary>How long collecting the repository inventory took.</summary>
    public TimeSpan ReadDuration { get; }

    /// <summary>Opens the repository containing <paramref name="path"/>, or explains why it could not.</summary>
    public static (GitRepository? Repository, string? Failure) Open(string path)
    {
        if (!Directory.Exists(path))
        {
            return (null, $"Path '{path}' does not exist or is not a directory.");
        }

        var topLevel = ProcessRunner.Run("git", ["rev-parse", "--show-toplevel"], path);
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

        var listing = ProcessRunner.Run("git", ["ls-files", "--stage", "-z"], rootPath);
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

    /// <summary>Reads the target of a tracked symbolic link from its staged blob.</summary>
    public (string? Target, string? Failure) ReadSymbolicLinkTarget(TrackedEntry entry)
    {
        var (text, failure) = ReadBlob(entry);
        return (text?.Trim(), failure);
    }

    /// <summary>
    /// Reads the text of a tracked document. The working tree is preferred, so the harness
    /// judges what the author is about to commit; when the file is absent there the staged
    /// blob is used, so a deleted-but-tracked document still has readable evidence.
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

        var blob = ProcessRunner.Run("git", ["cat-file", "blob", entry.ObjectId], RootPath);
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
            // "<mode> <object> <stage>\t<path>"
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

namespace Harness.Repository;

/// <summary>One entry of the Git index.</summary>
/// <param name="Path">Repository-relative path, always with '/' separators.</param>
/// <param name="Mode">Git file mode, for example 100644 for a regular file and 120000 for a symbolic link.</param>
/// <param name="ObjectId">Blob identifier of the staged content.</param>
internal sealed record TrackedEntry(string Path, string Mode, string ObjectId)
{
    public bool IsSymbolicLink => Mode == "120000";
}

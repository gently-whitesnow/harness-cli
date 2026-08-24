namespace Harness.Tests;

/// <summary>
/// The release number as the build stamps it. Tests read the one file that declares it
/// instead of restating the number, so a version that only half-propagates fails here.
/// </summary>
public static class Release
{
    private const string OpeningTag = "<HarnessVersion>";

    private const string ClosingTag = "</HarnessVersion>";

    public static string Current { get; } = ReadDeclaredVersion();

    public static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harness.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the solution directory.");
    }

    private static string ReadDeclaredVersion()
    {
        var path = Path.Combine(RepositoryRoot(), "Version.props");
        var text = File.ReadAllText(path);

        var start = text.IndexOf(OpeningTag, StringComparison.Ordinal);
        var end = text.IndexOf(ClosingTag, StringComparison.Ordinal);
        if (start < 0 || end < start)
        {
            throw new InvalidOperationException($"'{path}' does not declare {OpeningTag}.");
        }

        return text[(start + OpeningTag.Length)..end].Trim();
    }
}

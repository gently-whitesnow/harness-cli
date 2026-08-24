namespace Harness.Tests;

/// <summary>A disposable directory outside any repository.</summary>
public class TemporaryDirectory : IDisposable
{
    protected TemporaryDirectory(string path) => Path = path;

    public string Path { get; }

    public static TemporaryDirectory Create() => new(CreatePath());

    public string Absolute(string relativePath)
        => System.IO.Path.Combine(Path, relativePath.Replace('/', System.IO.Path.DirectorySeparatorChar));

    public void Dispose()
    {
        GC.SuppressFinalize(this);
        try
        {
            Directory.Delete(Path, recursive: true);
        }
        catch (DirectoryNotFoundException)
        {
        }
    }

    protected static string CreatePath()
    {
        var path = System.IO.Path.Combine(
            System.IO.Path.GetTempPath(),
            "harness-fixture-" + Guid.NewGuid().ToString("n"));
        Directory.CreateDirectory(path);
        return path;
    }
}

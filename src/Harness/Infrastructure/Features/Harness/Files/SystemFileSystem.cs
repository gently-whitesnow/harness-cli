using System.Text;
using Harness.Contracts.Files;

namespace Harness.Files;

internal sealed class SystemFileSystem : IFileSystem
{
    public bool Exists(string path) => File.Exists(path);

    public IReadOnlyList<string> EnumerateEntries(string path)
        => Directory.EnumerateFileSystemEntries(path).ToList();

    public string ReadText(string path) => File.ReadAllText(path);

    public void WriteText(string path, string content)
        => File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));

    public void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    public void Move(string source, string destination, bool overwrite)
        => File.Move(source, destination, overwrite);

    public void Delete(string path) => File.Delete(path);
}

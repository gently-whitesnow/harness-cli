namespace Harness.Contracts.Files;

internal interface IFileSystem
{
    bool Exists(string path);

    IReadOnlyList<string> EnumerateEntries(string path);

    string ReadText(string path);

    void WriteText(string path, string content);

    void WriteNew(string path, string content);

    void Move(string source, string destination, bool overwrite);

    void Delete(string path);
}

namespace Harness.Structure;

/// <summary>One declared type, and where a reader can open it.</summary>
internal sealed record TypeNode(string Subject, string Name, string Module, string Path, int Line)
{
    public string Location => $"{Path}:{Line}";
}

namespace Harness.Languages.CSharp;

/// <summary>
/// What one file declares: its declarations in source order, and the namespaces it imports.
/// </summary>
internal sealed record CSharpStructure(
    IReadOnlyList<Declaration> Declarations,
    IReadOnlyList<string> Imports)
{
    public int UsingDirectives => Imports.Count;
}

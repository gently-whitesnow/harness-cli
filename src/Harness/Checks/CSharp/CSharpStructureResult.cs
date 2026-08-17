namespace Harness.Checks.CSharp;

internal sealed record CSharpStructure(IReadOnlyList<Declaration> Declarations, int UsingDirectives);

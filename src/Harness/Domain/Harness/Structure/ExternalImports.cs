namespace Harness.Structure;

/// <summary>How many namespaces one file imports from outside the repository.</summary>
internal sealed record ExternalImports(string Path, int Count);

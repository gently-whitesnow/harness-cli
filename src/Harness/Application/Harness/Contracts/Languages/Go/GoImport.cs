namespace Harness.Languages.Go;

/// <summary>One import path and the line that names it.</summary>
internal sealed record GoImport(string ImportPath, int Line);

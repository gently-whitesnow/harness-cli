namespace Harness.Infrastructure.Languages.TypeScript;

internal sealed record TypeScriptImport(string Specifier, int Line, bool TypeOnly, string? ImportedName, bool Reexport, bool Dynamic,
    string? SourceName = null, string? LocalName = null);

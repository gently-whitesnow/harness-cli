namespace Harness.Languages.Go;

/// <summary>
/// One tracked Go file, reduced to what the checks read: the package it declares, the paths it
/// imports, the masked text with its literal and comment regions, the two comment-density
/// counts, and every line comment with the line it sits on.
/// </summary>
internal sealed record GoFile(
    string Path,
    string Package,
    IReadOnlyList<GoImport> Imports,
    string Masked,
    IReadOnlyList<MaskedRegion> Regions,
    int CommentLines,
    int AuthoredLines,
    IReadOnlyList<GoComment> Comments)
{
    public bool IsTest => Path.EndsWith("_test.go", StringComparison.Ordinal);

    public string Directory
    {
        get
        {
            var separator = Path.LastIndexOf('/');
            return separator < 0 ? string.Empty : Path[..separator];
        }
    }
}

/// <summary>One import path and the line that names it.</summary>
internal sealed record GoImport(string ImportPath, int Line);

/// <summary>A `//` comment: the text after the slashes, trimmed, and its physical line.</summary>
internal sealed record GoComment(int Line, string Text);

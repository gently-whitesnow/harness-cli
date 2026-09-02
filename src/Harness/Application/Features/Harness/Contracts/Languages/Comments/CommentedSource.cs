namespace Harness.Languages.Comments;

/// <summary>
/// One tracked file reduced to the two counts the comment density rule compares: physical
/// lines carrying any part of a comment, and non-empty lines the author wrote.
/// </summary>
internal sealed record CommentedSource(string Path, int CommentLines, int AuthoredLines);

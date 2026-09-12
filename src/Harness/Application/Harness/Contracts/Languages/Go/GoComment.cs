namespace Harness.Languages.Go;

/// <summary>A `//` comment: the text after the slashes with its leading whitespace kept, and its physical line.</summary>
internal sealed record GoComment(int Line, string Text);

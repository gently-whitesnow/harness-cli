namespace Harness.Languages.Duplication;

/// <summary>One physical line reduced to its token shape: the line number, the tokens joined by one space, and how many there are.</summary>
internal sealed record NormalizedLine(int Line, string Tokens, int TokenCount);

namespace Harness.Config;

internal sealed record FrameAnswer(
    string Key,
    FrameAnswerKind Kind,
    IReadOnlyList<string> Paths,
    string? Reason);

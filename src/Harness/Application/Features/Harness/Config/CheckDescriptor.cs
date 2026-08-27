namespace Harness.Config;

/// <summary>
/// Everything the frame needs to know about one shipped check: what it can be named by, which
/// applicability it shares, and — for a frame question — which answer key it reads. The
/// frame validates a document against this description and
/// never against the checks themselves, so reading a repository's answers does not depend on
/// being able to run anything.
/// </summary>
internal sealed record CheckDescriptor(
    string Id,
    string Group,
    string? Applicability,
    string? AnswerKey);

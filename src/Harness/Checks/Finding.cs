namespace Harness.Checks;

/// <param name="Expected">
/// Repository-relative paths the check looked for as tracked evidence and did not find, or
/// `*`-prefixed suffix patterns such as `*.slnx` when any path of that shape would have done.
/// It says nothing about the verdict: the run reports the same finding either way. It only
/// lets the report tell "the file is not written" apart from "the file is written but not
/// staged", which the harness cannot see and therefore treats as absent.
/// </param>
internal sealed record Finding(
    FindingSeverity Severity,
    string Location,
    string Message,
    IReadOnlyList<string>? Expected = null);

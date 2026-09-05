using Harness.Config;

namespace Harness.Checks.Frame;

/// <summary>One self-reported question that every repository answers in its harness frame.</summary>
internal sealed class FrameQuestionCheck(FrameQuestion question) : IRepositoryCheck
{
    private const int ShownLocations = 3;

    /// <summary>
    /// Navigation names entry points, not contents. A longer list is an inventory the reader
    /// has to fold back into the projects or directories that own it.
    /// </summary>
    private const int MaximumLocations = 5;

    private string Key => question.Key;

    private string Subject => question.Subject;

    internal string AnswerKey => Key;

    public string Id => $"{HarnessConfig.FrameGroup}.{Key}";

    public string Group => HarnessConfig.FrameGroup;

    /// <summary>A frame question is answered in the frame; it looks up no file of its own.</summary>
    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => question.Summary;

    public string Explanation => question.Explanation;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        if (context.Config is null)
        {
            return CheckEvaluation.Incomplete(
                $"the harness frame could not be read, so this repository's answer about {Subject} is unknown: "
                    + context.ConfigFailure);
        }

        var failure = context.Config.AnswerFailure(Key);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(AnswerRemediation(failure));
        }

        var answer = context.Config.Answered(Key);
        return answer?.Kind switch
        {
            FrameAnswerKind.Located => Located(answer),
            FrameAnswerKind.Present when question.RequiresLocation => CheckEvaluation.Incomplete(
                $"`answers.{Key}` must use `paths` so readers can run {Subject}; a positive answer without "
                    + "an address is not complete."),
            FrameAnswerKind.Present => CheckEvaluation.Passed(
                $"repository answers present — \"{answer.Reason}\". The answer is self-reported and not "
                    + "fact-checked by the harness."),
            FrameAnswerKind.Absent => CheckEvaluation.ReadinessGap(
                $"repository answers absent — \"{answer.Reason}\"."),
            FrameAnswerKind.NotApplicable when question.AppliesToEveryRepository => CheckEvaluation.Incomplete(
                $"`answers.{Key}` cannot be not applicable: every repository can own {Subject}. "
                    + "Use `present: false` with a reason until it does."),
            FrameAnswerKind.NotApplicable => CheckEvaluation.NotApplicable(
                $"repository answers not applicable — \"{answer.Reason}\"."),
            _ => CheckEvaluation.Incomplete(
                $"the validated harness frame has no answer for `answers.{Key}`; this is an internal error."),
        };
    }

    private string AnswerRemediation(string failure)
        => $$"""
        {{failure}}
        Investigate the repository before answering `answers.{{Key}}`. Choose the form that states what is true:
          "{{Key}}": { "paths": ["path/to/capability"] }
          "{{Key}}": { "present": true, "reason": "where or how it is provided" }
          "{{Key}}": { "present": false, "reason": "why it is currently absent" }
          "{{Key}}": { "applicable": false, "reason": "why this question does not apply" }
        `paths` names at most {{MaximumLocations}} entry points — a project or a directory, never the files inside it.
        Run `harness explain frame.{{Key}}` for the question's intent. Do not invent a positive answer. If the
        repository owner's intent is unclear, ask them before choosing an answer.
        """;

    private CheckEvaluation Located(FrameAnswer answer)
    {
        if (question.AddressesTestProjects)
        {
            var files = answer.Paths.Where(TestSuiteAddress.IsSourceFile).ToList();
            if (files.Count > 0)
            {
                return CheckEvaluation.Incomplete(
                    $"`answers.{Key}.paths` names test files instead of the projects that own them "
                        + $"({Locations(files)}). A reader runs a test project, not a file: name each project "
                        + $"once, for example {TestSuiteAddress.Owners(files)}.");
            }
        }

        if (answer.Paths.Count > MaximumLocations)
        {
            return CheckEvaluation.Incomplete(
                $"`answers.{Key}.paths` lists {answer.Paths.Count} addresses; navigation names at most "
                    + $"{MaximumLocations} entry points. Name the projects or directories that own {Subject}, "
                    + "not their contents.");
        }

        return CheckEvaluation.Passed(
            $"repository answers present at {Locations(answer.Paths)}. These paths are navigation for readers; "
                + "the harness does not inspect or fact-check them.");
    }

    private static string Locations(IReadOnlyList<string> paths)
    {
        var shown = string.Join(", ", paths.Take(ShownLocations));
        return paths.Count <= ShownLocations
            ? shown
            : $"{shown} and {paths.Count - ShownLocations} more ({paths.Count} total)";
    }
}

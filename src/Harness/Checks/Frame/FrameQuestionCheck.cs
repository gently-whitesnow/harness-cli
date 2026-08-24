using Harness.Config;

namespace Harness.Checks.Frame;

/// <summary>One self-reported question that every repository answers in its harness frame.</summary>
internal abstract class FrameQuestionCheck : IRepositoryCheck
{
    private const int ShownLocations = 3;

    protected abstract string Key { get; }

    protected abstract string Subject { get; }

    internal string AnswerKey => Key;

    public string Id => $"{HarnessConfig.FrameGroup}.{Key}";

    public string Group => HarnessConfig.FrameGroup;

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

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
            FrameAnswerKind.Present => CheckEvaluation.Passed(
                $"repository answers present — \"{answer.Reason}\". The answer is self-reported and not "
                    + "fact-checked by the harness."),
            FrameAnswerKind.Absent => CheckEvaluation.ReadinessGap(
                $"repository answers absent — \"{answer.Reason}\"."),
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
        Run `harness explain frame.{{Key}}` for the question's intent. Do not invent a positive answer or use
        `suppress` to bypass an unanswered frame question. If the repository owner's intent is unclear, ask
        them before choosing an answer.
        """;

    private static CheckEvaluation Located(FrameAnswer answer)
        => CheckEvaluation.Passed(
            $"repository answers present at {Locations(answer.Paths)}. These paths are navigation for readers; "
                + "the harness does not inspect or fact-check them.");

    private static string Locations(IReadOnlyList<string> paths)
    {
        var shown = string.Join(", ", paths.Take(ShownLocations));
        return paths.Count <= ShownLocations
            ? shown
            : $"{shown} and {paths.Count - ShownLocations} more ({paths.Count} total)";
    }
}

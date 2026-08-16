using System.Globalization;
using Harness.Checks.CSharp;

namespace Harness.Checks.Comments;

internal sealed class CommentLineCheck : IRepositoryCheck
{
    public string Id => "comments.csharp";

    public string Group => "comments";

    public string Summary => "C# comment density limit";

    public string Explanation => CommentLineExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (sources, failure) = CSharpSources.Discover(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (sources.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var findings = sources
            .Where(ExceedsLimit)
            .Select(source => new Finding(
                FindingSeverity.Blocking,
                source.Path,
                $"{source.CommentLines} of {source.AuthoredLines} authored physical lines are comments "
                    + $"({Percentage(source)}%), above the {CommentLineExplanation.PercentageLimit}% limit "
                    + $"after the minimum of {CommentLineExplanation.MinimumCommentLines} comment lines; "
                    + "keep comments only for a non-obvious reason, "
                    + "invariant, workaround, or required public API contract, and express the rest in names "
                    + "and structure"))
            .ToList();

        return CheckEvaluation.From(findings);
    }

    private static bool ExceedsLimit(CSharpSource source)
        => source.CommentLines >= CommentLineExplanation.MinimumCommentLines
            && source.CommentLines * 100
                > source.AuthoredLines * CommentLineExplanation.PercentageLimit;

    private static string Percentage(CSharpSource source)
        => (100m * source.CommentLines / source.AuthoredLines)
            .ToString("F1", CultureInfo.InvariantCulture);
}

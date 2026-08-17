using System.Globalization;
using Harness.Checks.CSharp;
using Harness.Config;

namespace Harness.Checks.Comments;

internal sealed class CommentLineCheck : IRepositoryCheck
{
    public string Id => "comments.csharp";

    public string Group => "comments";

    public string Applicability => "csharp";

    public string Summary => "C# comment density limit";

    public string Explanation => CommentLineExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var settings = context.Config?.Settings.Comments ?? CommentSettings.Default;
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
            .Where(source => ExceedsLimit(source, settings))
            .Select(source => new Finding(
                FindingSeverity.Blocking,
                source.Path,
                $"{source.CommentLines} of {source.AuthoredLines} authored physical lines are comments "
                    + $"({Percentage(source)}%), above the {settings.PercentageLimit}% limit "
                    + $"after the minimum of {settings.MinimumCommentLines} comment lines; "
                    + "keep comments only for a non-obvious reason, "
                    + "invariant, workaround, or required public API contract, and express the rest in names "
                    + "and structure"))
            .ToList();

        return CheckEvaluation.From(findings);
    }

    private static bool ExceedsLimit(CSharpSource source, CommentSettings settings)
        => source.CommentLines >= settings.MinimumCommentLines
            && (long)source.CommentLines * 100 > (long)source.AuthoredLines * settings.PercentageLimit;

    private static string Percentage(CSharpSource source)
        => (100m * source.CommentLines / source.AuthoredLines)
            .ToString("F1", CultureInfo.InvariantCulture);
}

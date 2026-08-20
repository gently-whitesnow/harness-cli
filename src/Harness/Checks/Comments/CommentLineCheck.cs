using System.Globalization;
using Harness.Config;
using Harness.Languages;
using Harness.Languages.CSharp;

namespace Harness.Checks.Comments;

internal sealed class CommentLineCheck(CSharpSources sources) : IRepositoryCheck
{
    public string Id => Language.CSharp.Qualify("comments");

    public string Group => "comments";

    public string Applicability => Language.CSharp.Key;

    public string Summary => "C# comment density limit";

    public string Explanation => CommentLineExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var settings = context.Config?.Settings.Comments ?? CommentSettings.Default;
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var findings = files
            .Select(file => file.Source)
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

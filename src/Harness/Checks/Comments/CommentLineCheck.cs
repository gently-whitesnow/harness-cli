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
        var (files, failure) = sources.Read(context.Repository);
        if (failure is not null)
        {
            return CheckEvaluation.Incomplete(failure);
        }

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable(CSharpSources.NothingToAnalyze);
        }

        var analyzed = files
            .Select(file => file.Source)
            .Where(source => !OverrideResolution.Disables(context.Config, Id, source.Path))
            .ToList();
        if (analyzed.Count == 0)
        {
            return CheckEvaluation.NotApplicable(OverrideResolution.EverythingExcluded);
        }

        var findings = analyzed
            .Select(source => (Source: source,
                Settings: OverrideResolution.CommentsFor(context.Config, Id, source.Path)))
            .Where(entry => ExceedsLimit(entry.Source, entry.Settings))
            .Select(entry => new Finding(
                FindingSeverity.Blocking,
                entry.Source.Path,
                $"{entry.Source.CommentLines} of {entry.Source.AuthoredLines} authored physical lines "
                    + $"are comments ({Percentage(entry.Source)}%), above the {entry.Settings.PercentageLimit}% "
                    + $"limit after the minimum of {entry.Settings.MinimumCommentLines} comment lines; "
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

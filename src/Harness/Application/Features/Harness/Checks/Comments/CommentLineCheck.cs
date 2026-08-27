using System.Globalization;
using Harness.Config;
using Harness.Languages.CSharp;

namespace Harness.Checks.Comments;

internal sealed class CommentLineCheck(CSharpSources sources)
    : CSharpSourceCheck(sources, "comments", "C# comment density limit", CommentLineExplanation.Text)
{
    protected override CheckEvaluation Evaluate(CheckContext context, IReadOnlyList<CSharpFile> files)
    {
        var settings = context.Config?.Settings.Comments ?? CommentSettings.Default;
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

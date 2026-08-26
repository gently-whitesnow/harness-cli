using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Cohesion;

/// <summary>
/// Reports types whose members form unrelated state groups. Findings originate as advisory
/// evidence; required repository policy enforces the limit unless migration is advisory.
/// </summary>
internal sealed class CohesionCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "cohesion", "types that hold unrelated groups of members")
{
    private const int Shown = 5;

    public override string Explanation => CohesionExplanation.Text;

    public override CheckEvaluation Evaluate(CheckContext context)
    {
        var (types, failure) = Analyzer.ReadCohesion(context.Repository);
        if (types is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        if (types.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze);
        }

        var settings = context.Config?.Settings.Cohesion ?? CohesionSettings.Default;
        var metric = new Metric("independent member groups", settings.Groups);

        var report = MetricReport.Exceeding(Measure(types, metric, settings), [metric], Shown);
        return CheckEvaluation.From(report.Summary, detailedFindings: report.Detailed);
    }

    /// <summary>
    /// A type with no state cannot lack cohesion in the sense measured here, and a type with
    /// few members says nothing either way. Both are left out rather than counted as ones.
    /// </summary>
    private static List<Measurement> Measure(
        IReadOnlyList<TypeCohesion> types,
        Metric metric,
        CohesionSettings settings)
        => types
            .Where(type => type.StateMembers > 0 && type.Members.Count >= settings.MinimumMembers)
            .Select(type => new Measurement(
                metric, MemberComponents.Of(type.Members).Count, type.Subject, type.Location))
            .ToList();
}

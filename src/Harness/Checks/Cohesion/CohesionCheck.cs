using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Cohesion;

/// <summary>
/// Splits each type into groups of members that share state and reports the types that hold
/// more than one such group. It is advisory and stays advisory: a type can hold two unrelated
/// groups on purpose, and no lexical reader can tell that apart from an accident.
/// </summary>
internal sealed class CohesionCheck(ILanguageAnalyzer analyzer) : IRepositoryCheck
{
    private const int Shown = 5;

    public string Id => analyzer.Language.Qualify("cohesion");

    public string Group => "cohesion";

    public string Applicability => analyzer.Language.Key;

    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => $"{analyzer.Language.Name} types that hold unrelated groups of members";

    public string Explanation => CohesionExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var (types, failure) = analyzer.ReadCohesion(context.Repository);
        if (types is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        if (types.Count == 0)
        {
            return CheckEvaluation.NotApplicable(analyzer.NothingToAnalyze);
        }

        var settings = context.Config?.Settings.Cohesion ?? CohesionSettings.Default;
        var metric = new Metric("independent member groups", settings.Groups);

        return CheckEvaluation.From(
            MetricReport.Exceeding(Measure(types, metric, settings), [metric], Shown));
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

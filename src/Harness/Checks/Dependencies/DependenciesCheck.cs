using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Dependencies;

/// <summary>
/// Reads the dependency graph a repository declares about itself and reports two different
/// kinds of thing. A cycle between modules is proved: every edge of it stands in a position
/// the language allows nothing but a type, and every name in it resolves to exactly one
/// declaration, so the check itself marks it blocking. The counts around it are approximate
/// and originate as advisory evidence; the repository policy decides whether they block.
/// </summary>
internal sealed class DependenciesCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "dependencies", "dependencies between modules and types")
{
    private const int ShownPerMetric = 5;

    private const int ShownCycles = 3;

    private const int ShownEdges = 4;

    public override string Explanation => DependenciesExplanation.Text;

    public override CheckEvaluation Evaluate(CheckContext context)
    {
        var (graph, failure) = Analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        if (graph.Types.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze);
        }

        var settings = context.Config?.Settings.Dependencies ?? DependencySettings.Default;
        var findings = Cycles(graph).Concat(Coupling(graph, settings)).ToList();

        return CheckEvaluation.From(findings, Coverage(graph));
    }

    private static string Coverage(SourceGraph graph)
        => $"{graph.CoveragePercentage}% of the {graph.CandidateReferences} names that match a declared type "
            + "resolved to exactly one of them; the rest are not in the graph.";

    private static IEnumerable<Finding> Cycles(SourceGraph graph)
    {
        var cycles = ModuleGraph.Cycles(graph.Proven);
        foreach (var cycle in cycles.Take(ShownCycles))
        {
            yield return new Finding(FindingSeverity.Blocking, cycle.Location, Describe(cycle));
        }

        if (cycles.Count > ShownCycles)
        {
            yield return new Finding(
                FindingSeverity.Blocking,
                cycles[ShownCycles].Location,
                $"{cycles.Count} module dependency cycles were proved; the first {ShownCycles} are listed above");
        }
    }

    private static string Describe(ModuleCycle cycle)
    {
        var evidence = cycle.Path
            .Take(ShownEdges)
            .Select(edge => $"{edge.From.Subject} names {edge.To.Subject} at {edge.Location}");
        var remaining = cycle.Path.Count - Math.Min(cycle.Path.Count, ShownEdges);
        var wider = cycle.Modules.Count > cycle.Path.Count
            ? $" It is the shortest ring inside a group of {cycle.Modules.Count} modules that all reach "
                + "each other, so more will surface once it is broken."
            : string.Empty;

        return $"module dependency cycle {string.Join(" -> ", cycle.Closed)}: "
            + string.Join("; ", evidence)
            + (remaining > 0 ? $"; and {remaining} more steps" : string.Empty)
            + ". These modules cannot be read, moved or reused in one direction until one of these "
            + "references is turned around or the concept both need is moved out of both." + wider;
    }

    private static List<Finding> Coupling(SourceGraph graph, DependencySettings settings)
    {
        var outgoing = new Metric("resolved outgoing type references", settings.OutgoingReferences);
        var incoming = new Metric("resolved incoming type references", settings.IncomingReferences);
        var external = new Metric("external import fan-out", settings.ExternalImports);

        var measurements = new List<Measurement>();
        measurements.AddRange(Fan(graph, outgoing, edge => edge.From));
        measurements.AddRange(Fan(graph, incoming, edge => edge.To));
        measurements.AddRange(graph.Imports.Select(file =>
            new Measurement(external, file.Count, file.Path, file.Path)));

        return MetricReport.Exceeding(measurements, [outgoing, incoming, external], ShownPerMetric);
    }

    // One edge per pair of types already, so counting edges counts distinct partners.
    private static IEnumerable<Measurement> Fan(
        SourceGraph graph,
        Metric metric,
        Func<ReferenceEdge, TypeNode> end)
        => graph.Edges
            .GroupBy(end)
            .Select(group => new Measurement(metric, group.Count(), group.Key.Subject, group.Key.Location));
}

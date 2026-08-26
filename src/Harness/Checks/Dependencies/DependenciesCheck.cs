using Harness.Checks.Metrics;
using Harness.Config;
using Harness.Languages;
using Harness.Structure;
using Harness.Versioning;

namespace Harness.Checks.Dependencies;

/// <summary>
/// Reads the dependency graph a repository declares about itself and reports two different
/// kinds of thing. A current-contract cycle between modules is proved: every edge of it
/// stands in a position the language allows nothing but a type, and every name resolves to
/// exactly one declaration. Legacy pins retain the counts they originally contracted for.
/// </summary>
internal sealed class DependenciesCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "dependencies", "dependencies between modules and types")
{
    private static readonly HarnessVersion CountsRemovedSince = new(1, 5, 0);

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

        var cycles = Cycles(graph);
        if (context.Config?.Includes(CountsRemovedSince) == true)
        {
            return CheckEvaluation.From(cycles.Summary, Coverage(graph), cycles.Detailed);
        }

        var settings = context.Config?.Settings.Dependencies ?? DependencySettings.Default;
        var coupling = Coupling(graph, settings);
        var findings = cycles.Summary.Concat(coupling.Summary).ToList();
        var detailed = cycles.Detailed.Concat(coupling.Detailed).ToList();

        return CheckEvaluation.From(findings, Coverage(graph), detailed);
    }

    private static string Coverage(SourceGraph graph)
        => $"{graph.CoveragePercentage}% of the {graph.CandidateReferences} names that match a declared type "
            + "resolved to exactly one of them; the rest are not in the graph.";

    private static FindingSet Cycles(SourceGraph graph)
    {
        var cycles = ModuleGraph.Cycles(graph.Proven);
        var detailed = cycles
            .Select(cycle => new Finding(FindingSeverity.Blocking, cycle.Location, Describe(cycle)))
            .ToList();
        var summary = detailed.Take(ShownCycles).ToList();

        if (cycles.Count > ShownCycles)
        {
            summary.Add(new Finding(
                FindingSeverity.Blocking,
                cycles[ShownCycles].Location,
                $"{cycles.Count} module dependency cycles were proved; the first {ShownCycles} are listed above"));
        }

        return new FindingSet(summary, detailed);
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

    private static MetricFindings Coupling(SourceGraph graph, DependencySettings settings)
    {
        var outgoing = new Metric("resolved outgoing type references", settings.OutgoingReferences);
        var incoming = new Metric("resolved incoming type references", settings.IncomingReferences);
        var external = new Metric("external import fan-out", settings.ExternalImports);

        var measurements = new List<Measurement>();
        measurements.AddRange(Fan(graph.Edges, outgoing, edge => edge.From));
        measurements.AddRange(Fan(graph.Edges, incoming, edge => edge.To));
        measurements.AddRange(graph.Imports.Select(file =>
            new Measurement(external, file.Count, file.Path, file.Path)));

        return MetricReport.Exceeding(
            measurements,
            [outgoing, incoming, external],
            ShownPerMetric);
    }

    // One edge per pair of types already, so counting edges counts distinct partners.
    private static IEnumerable<Measurement> Fan(
        IEnumerable<ReferenceEdge> edges,
        Metric metric,
        Func<ReferenceEdge, TypeNode> end)
        => edges
            .GroupBy(end)
            .Select(group => new Measurement(metric, group.Count(), group.Key.Subject, group.Key.Location));

    private sealed record FindingSet(
        IReadOnlyList<Finding> Summary,
        IReadOnlyList<Finding> Detailed);
}

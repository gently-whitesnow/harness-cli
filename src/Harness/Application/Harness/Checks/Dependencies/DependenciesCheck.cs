using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Dependencies;

/// <summary>
/// Reads the dependency graph a repository declares about itself. A cycle between modules
/// is proved: every edge of it
/// stands in a position the language allows nothing but a type, and every name resolves to
/// exactly one declaration.
/// </summary>
internal sealed class DependenciesCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "dependencies", "dependencies between modules and types")
{
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
        return CheckEvaluation.From(cycles.Summary, Coverage(graph), cycles.Detailed);
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

    private sealed record FindingSet(
        IReadOnlyList<Finding> Summary,
        IReadOnlyList<Finding> Detailed);
}

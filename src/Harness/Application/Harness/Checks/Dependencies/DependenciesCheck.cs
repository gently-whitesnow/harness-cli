using Harness.Languages;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Checks.Dependencies;

/// <summary>
/// Reads the dependency graph a repository declares about itself. A cycle between modules
/// is proved: every edge of it stands in a position the language allows nothing but a
/// declaration, and every name resolves to exactly one. For C# the module is a namespace and
/// the edge a type-only position; for Ansible the module is a role and the edge a literal
/// role name in `meta` or an include.
/// </summary>
internal sealed class DependenciesCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "dependencies", Subject(analyzer))
{
    private const int ShownCycles = 3;

    private const int ShownEdges = 4;

    public override IReadOnlyList<EvidenceFile> Evidence => Analyzer.NamedEvidence.Select(name => new EvidenceFile(name)).ToList();

    public override string Explanation => DependenciesExplanation.For(Analyzer.Language);

    private string Module => Analyzer.Language == Language.Ansible ? "role" : "module";

    private static string Subject(ILanguageAnalyzer analyzer)
        => analyzer.Language == Language.Ansible ? "dependencies between roles" : "dependencies between modules and types";

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
        return CheckEvaluation.From(cycles.Summary, Coverage(graph), cycles.Detailed, graph.Details);
    }

    private string Coverage(SourceGraph graph)
        => Analyzer.Language == Language.TypeScript
            ? $"{graph.CoveragePercentage}% of {graph.CandidateReferences} literal TypeScript/JavaScript imports resolved; unresolved imports are listed in details."
            : Analyzer.Language == Language.Ansible
            ? $"{graph.CoveragePercentage}% of the {graph.CandidateReferences} role names in meta dependencies and includes "
                + "are literals; a name written in Jinja is Inferred and not in the graph."
            : $"{graph.CoveragePercentage}% of the {graph.CandidateReferences} names that match a declared type "
                + "resolved to exactly one of them; the rest are not in the graph.";

    private FindingSet Cycles(SourceGraph graph)
    {
        var edges = Analyzer.Language == Language.TypeScript
            ? graph.Proven.Where(edge => edge.From.Module != edge.To.Module)
            : graph.Proven;
        var cycles = ModuleGraph.Cycles(edges, collapseNestedModules: Analyzer.Language != Language.Ansible && Analyzer.Language != Language.TypeScript);
        var detailed = cycles
            .Select(cycle => new Finding(FindingSeverity.Blocking, cycle.Location, Describe(cycle)))
            .ToList();
        var summary = detailed.Take(ShownCycles).ToList();

        if (cycles.Count > ShownCycles)
        {
            summary.Add(new Finding(
                FindingSeverity.Blocking,
                cycles[ShownCycles].Location,
                $"{cycles.Count} {Module} dependency cycles were proved; the first {ShownCycles} are listed above"));
        }

        return new FindingSet(summary, detailed);
    }

    private string Describe(ModuleCycle cycle)
    {
        var evidence = cycle.Path
            .Take(ShownEdges)
            .Select(edge => $"{edge.From.Subject} names {edge.To.Subject} at {edge.Location}{(edge.TypeOnly ? " (type-only)" : "")}");
        var remaining = cycle.Path.Count - Math.Min(cycle.Path.Count, ShownEdges);
        var wider = cycle.Modules.Count > cycle.Path.Count
            ? $" It is the shortest ring inside a group of {cycle.Modules.Count} {Module}s that all reach "
                + "each other, so more will surface once it is broken."
            : string.Empty;

        return $"{Module} dependency cycle {string.Join(" -> ", cycle.Closed)}: "
            + string.Join("; ", evidence)
            + (remaining > 0 ? $"; and {remaining} more steps" : string.Empty)
            + $". These {Module}s cannot be read, moved or reused in one direction until one of these "
            + "references is turned around or the concept both need is moved out of both." + wider;
    }

    private sealed record FindingSet(
        IReadOnlyList<Finding> Summary,
        IReadOnlyList<Finding> Detailed);
}

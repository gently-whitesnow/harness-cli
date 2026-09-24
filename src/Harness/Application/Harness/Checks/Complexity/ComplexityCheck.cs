using System.Globalization;
using Harness.Config;
using Harness.Languages;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed class ComplexityCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "complexity", "repository DSM complexity")
{
    private const int NamedHubs = 5;

    // C# draws the product boundary from project files; Go resolves imports through go.mod.
    public override IReadOnlyList<EvidenceFile> Evidence =>
    [
        .. Analyzer.NamedEvidence.Select(name => new EvidenceFile(name)),
        .. Analyzer.Language == Language.CSharp ? DotNetRepository.ProjectFiles : Array.Empty<EvidenceFile>(),
    ];

    public override string Explanation => ComplexityExplanation.For(Analyzer.Language);

    public override CheckEvaluation Evaluate(CheckContext context)
    {
        var (graph, failure) = Analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        if (graph.SourcePaths.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze);
        }

        var (scope, scopeFailure) = Scope(context, graph);
        if (scope is null)
        {
            return CheckEvaluation.Incomplete(scopeFailure!);
        }

        var unit = Analyzer.Unit;
        var metric = RepositoryComplexity.Measure(scope.Graph);
        var limit = context.Config?.Settings.ComplexityFor(Analyzer.Language) ?? ComplexitySettings.Default;
        var details = new List<string>
        {
            $"limits: average reachable {unit}s {Units(limit.AverageReachableFiles, unit)} · largest cyclic group size {limit.LargestCyclicGroupSize} {unit}s",
            $"average reachable {unit}s: {Units(metric.AverageReachableFiles, unit)} "
                + $"({metric.ReachablePairs} reachable {unit} pairs / {metric.AuthoredFiles} {unit}s; "
                + $"propagation cost {Percent(metric.PropagationCostPercentage)})",
            $"largest cyclic group size: {metric.LargestCyclicGroupSize} {unit}s "
                + $"({Percent(metric.LargestCyclicGroupPercentage)} of {metric.AuthoredFiles} {unit}s)",
            scope.Describe(),
        };
        details.AddRange(graph.Details);
        if (scope.DescribeMarkedGenerated() is { } marked)
        {
            details.Add(marked);
        }

        if (scope.DescribeMarkedIgnored() is { } ignored)
        {
            details.Add(ignored);
        }

        var findings = new List<Finding>();
        if (metric.AverageReachableFiles > limit.AverageReachableFiles)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                scope.Location,
                $"average reachable {unit}s {Units(metric.AverageReachableFiles, unit)} exceeds the {Units(limit.AverageReachableFiles, unit)} the "
                    + "standard allows; cut edges from the hubs named below or change the tracked policy knowingly."));
            findings.AddRange(Hubs(scope, unit));
        }

        if (metric.LargestCyclicGroupSize > limit.LargestCyclicGroupSize)
        {
            findings.AddRange(RepositoryComplexity.LargestCyclicGroup(scope.Graph).Select(path => new Finding(
                FindingSeverity.Blocking,
                path,
                $"This {unit} belongs to the largest SCC ({metric.LargestCyclicGroupSize} {unit}s); the standard allows "
                    + $"{limit.LargestCyclicGroupSize} — break the cycle.")));
        }

        return CheckEvaluation.From(findings, details: details);
    }

    private (DsmScope? Scope, string? Failure) Scope(CheckContext context, SourceGraph graph)
    {
        if (Analyzer.Language == Language.TypeScript)
        {
            return (DsmScope.OfTypeScript(graph), null);
        }

        if (Analyzer.Language == Language.Go)
        {
            return (DsmScope.OfPackages(graph), null);
        }

        var (projects, projectFailure) = DotNetRepository.ReadProjects(context);
        if (projectFailure is not null)
        {
            return (null, projectFailure);
        }

        return (DsmScope.Of(
            graph,
            context.Config?.Architecture is { IsApplicable: true },
            context.Repository.TrackedEntries.Where(entry => !entry.IsSymbolicLink).Select(entry => entry.Path).ToList(),
            projects.Select(project => (project.Path, DotNetRepository.IsTestProject(project))).ToList()), null);
    }

    /// <summary>
    /// The nodes whose own reach is largest, outside the composition root: Host, or a Go `main`
    /// package, is expected to see the whole product, so naming it would tell the reader
    /// nothing they can act on.
    /// </summary>
    private static IEnumerable<Finding> Hubs(DsmScope scope, string unit)
    {
        var total = scope.Graph.SourcePaths.Count;
        return RepositoryComplexity.FileReaches(scope.Graph)
            .Where(file => !scope.IsCompositionRoot(file.Path))
            .Take(NamedHubs)
            .Select(file => new Finding(
                FindingSeverity.Blocking,
                file.Path,
                $"Dependencies from here reach {file.Files} of {total} {unit}s."));
    }

    private static string Units(double value, string unit)
        => value.ToString("F2", CultureInfo.InvariantCulture) + " " + unit + "s";

    private static string Percent(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + "%";
}

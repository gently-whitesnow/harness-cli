using System.Globalization;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed class ComplexityCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "complexity", "repository DSM complexity")
{
    public override IReadOnlyList<EvidenceFile> Evidence => DotNetRepository.ProjectFiles;

    public override string Explanation => ComplexityExplanation.Text;

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

        var (projects, projectFailure) = DotNetRepository.ReadProjects(context);
        if (projectFailure is not null)
        {
            return CheckEvaluation.Incomplete(projectFailure);
        }

        var scope = DsmScope.Of(
            graph,
            context.Config?.Architecture is { IsApplicable: true },
            context.Repository.TrackedEntries.Where(entry => !entry.IsSymbolicLink).Select(entry => entry.Path).ToList(),
            projects.Select(project => (project.Path, DotNetRepository.IsTestProject(project))).ToList());
        var metric = RepositoryComplexity.Measure(scope.Graph);
        var observations = new List<string>
        {
            $"limits: mean reach {Files(ComplexityLimit.MeanReach)} · core size {ComplexityLimit.CoreSize} files",
            $"mean reach: {Files(metric.MeanReach)} "
                + $"({metric.ReachablePairs} reachable file pairs / {metric.AuthoredFiles} files; "
                + $"propagation cost {Percent(metric.PropagationCostPercentage)})",
            $"core size: {metric.CoreFiles} files "
                + $"({Percent(metric.CorePercentage)} of {metric.AuthoredFiles} files)",
            scope.Describe(),
        };
        if (scope.DescribeMarkedGenerated() is { } marked)
        {
            observations.Add(marked);
        }

        var findings = new List<Finding>();
        if (metric.MeanReach > ComplexityLimit.MeanReach)
        {
            findings.Add(new Finding(
                FindingSeverity.Blocking,
                scope.Location,
                $"mean reach {Files(metric.MeanReach)} exceeds the {Files(ComplexityLimit.MeanReach)} the "
                    + "standard allows; cut edges from the hubs named below or change the tracked policy knowingly."));
            findings.AddRange(Hubs(scope));
        }

        if (metric.CoreFiles > ComplexityLimit.CoreSize)
        {
            findings.AddRange(RepositoryComplexity.LargestCore(scope.Graph).Select(path => new Finding(
                FindingSeverity.Blocking,
                path,
                $"This file belongs to the largest SCC ({metric.CoreFiles} files); the standard allows "
                    + $"{ComplexityLimit.CoreSize} — break the cycle.")));
        }

        return CheckEvaluation.From(findings, observations: observations);
    }

    /// <summary>
    /// The files whose own reach is largest, outside the composition root: Host is expected to
    /// see the whole product, so naming it would tell the reader nothing they can act on.
    /// </summary>
    private static IEnumerable<Finding> Hubs(DsmScope scope)
    {
        var total = scope.Graph.SourcePaths.Count;
        return RepositoryComplexity.FileReaches(scope.Graph)
            .Where(file => !scope.IsCompositionRoot(file.Path))
            .Take(ComplexityLimit.NamedHubs)
            .Select(file => new Finding(
                FindingSeverity.Blocking,
                file.Path,
                $"A change here reaches {file.Files} of {total} files."));
    }

    private static string Files(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + " files";

    private static string Percent(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + "%";
}

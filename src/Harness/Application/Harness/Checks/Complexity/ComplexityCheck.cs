using System.Globalization;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed class ComplexityCheck(
    ILanguageAnalyzer analyzer,
    IReadOnlyList<ILanguageAnalyzer> budgetAnalyzers)
    : LanguageAnalyzerCheck(analyzer, "complexity", "repository DSM complexity")
{
    private static readonly EvidenceFile BudgetEvidence = new(ComplexityBudget.FileName);

    public override IReadOnlyList<EvidenceFile> Evidence => [BudgetEvidence, .. DotNetRepository.ProjectFiles];

    public override string Explanation => ComplexityExplanation.Text;

    public override CheckEvaluation Evaluate(CheckContext context)
    {
        var (graph, failure) = Analyzer.ReadGraph(context.Repository);
        if (graph is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        var budgetCheckIds = budgetAnalyzers
            .Where(candidate => context.Config?.NotApplicable(candidate.Language.Key) is null)
            .Select(candidate => candidate.Language.Qualify("complexity"))
            .ToList();
        var (budget, budgetFailure) = ComplexityBudget.Load(context, BudgetEvidence, budgetCheckIds);
        if (budget is null || !budget.Entries.TryGetValue(Id, out var limit))
        {
            return CheckEvaluation.Incomplete(budgetFailure!);
        }

        var budgetObservation = $"DSM budget: mean reach {Files(limit.MeanReach)} · core size {limit.CoreSize} files";
        if (graph.SourcePaths.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze, [budgetObservation]);
        }

        var (projects, projectFailure) = DotNetRepository.ReadProjects(context);
        if (projectFailure is not null)
        {
            return CheckEvaluation.Incomplete(projectFailure, [budgetObservation]);
        }

        var architectureApplicable = context.Config?.Architecture is { IsApplicable: true };
        var scope = DsmScope.Of(
            graph,
            architectureApplicable,
            context.Repository.TrackedEntries.Where(entry => !entry.IsSymbolicLink).Select(entry => entry.Path).ToList(),
            projects.Select(project => (project.Path, DotNetRepository.IsTestProject(project))).ToList());
        var metric = RepositoryComplexity.Measure(scope.Graph);
        var observations = new List<string>
        {
            budgetObservation,
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

        var current = ComplexityBudget.Entry.From(metric);
        if (current.MeanReach > limit.MeanReach || current.CoreSize > limit.CoreSize)
        {
            var findings = RegressionFindings(scope.Graph, limit, current);
            var regression = new Finding(
                FindingSeverity.Blocking,
                ComplexityBudget.FileName,
                RegressionMessage(limit, current));
            return CheckEvaluation.From(
                [regression, .. findings],
                detailedFindings: [regression, .. findings],
                observations: observations);
        }

        var progress = ProgressMessage(limit, current);
        return CheckEvaluation.From(
            [],
            observations: progress is null ? observations : [.. observations, progress]);
    }

    private static List<Finding> RegressionFindings(
        SourceGraph graph,
        ComplexityBudget.Entry budget,
        ComplexityBudget.Entry current)
    {
        var findings = new List<Finding>();
        if (current.MeanReach > budget.MeanReach)
        {
            findings.AddRange(RepositoryComplexity.HighestPropagationEdges(graph)
                .Take(5)
                .Select(edge => new Finding(
                    FindingSeverity.Blocking,
                    edge.Edge.Location,
                    $"Proven file edge {edge.Edge.From.Path} -> {edge.Edge.To.Path} "
                        + $"has a propagation span of {edge.ReachablePairs} reachable file pairs.")));
        }

        if (current.CoreSize > budget.CoreSize)
        {
            findings.AddRange(RepositoryComplexity.LargestCore(graph).Select(path => new Finding(
                FindingSeverity.Blocking,
                path,
                $"This file belongs to the largest SCC ({current.CoreSize} files).")));
        }

        return findings.Count == 0
            ? [new Finding(FindingSeverity.Blocking, ComplexityBudget.FileName, RegressionMessage(budget, current))]
            : findings;
    }

    private static string RegressionMessage(ComplexityBudget.Entry budget, ComplexityBudget.Entry current)
    {
        var deltas = new List<string>();
        if (current.MeanReach > budget.MeanReach)
        {
            deltas.Add($"mean reach +{Files(current.MeanReach - budget.MeanReach)}");
        }

        if (current.CoreSize > budget.CoreSize)
        {
            deltas.Add($"core size +{current.CoreSize - budget.CoreSize} files");
        }

        return $"DSM budget regressed ({string.Join(", ", deltas)}); reduce the graph or review the tracked budget manually.";
    }

    private static string? ProgressMessage(ComplexityBudget.Entry budget, ComplexityBudget.Entry current)
    {
        var progress = new List<string>();
        if (budget.MeanReach - current.MeanReach >= ComplexityBudget.NoticeableReachDelta)
        {
            progress.Add($"mean reach -{Files(budget.MeanReach - current.MeanReach)}");
        }

        if (current.CoreSize < budget.CoreSize)
        {
            progress.Add($"core size -{budget.CoreSize - current.CoreSize} files");
        }

        return progress.Count == 0
            ? null
            : $"DSM complexity improved ({string.Join(", ", progress)}); run `harness budget update` to record the progress.";
    }

    private static string Files(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + " files";

    private static string Percent(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + "%";
}

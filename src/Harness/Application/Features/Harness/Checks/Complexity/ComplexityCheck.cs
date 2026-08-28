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

    public override IReadOnlyList<EvidenceFile> Evidence => [BudgetEvidence];

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

        var budgetObservation = $"DSM budget: propagation cost {Percent(limit.PropagationCost)} · core size {limit.CoreSize} files";
        if (graph.SourcePaths.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze, [budgetObservation]);
        }

        var metric = RepositoryComplexity.Measure(graph);
        var possiblePairs = (long)metric.AuthoredFiles * metric.AuthoredFiles;
        var observations = new[]
        {
            budgetObservation,
            $"propagation cost: {Percent(metric.PropagationCostPercentage)} "
                + $"({metric.ReachablePairs} reachable file pairs / {possiblePairs})",
            $"core size: {metric.CoreFiles} files "
                + $"({Percent(metric.CorePercentage)} of {metric.AuthoredFiles} authored files)",
        };
        var current = ComplexityBudget.Entry.From(metric);
        if (current.PropagationCost > limit.PropagationCost || current.CoreSize > limit.CoreSize)
        {
            var findings = RegressionFindings(graph, limit, current);
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
        if (current.PropagationCost > budget.PropagationCost)
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
        if (current.PropagationCost > budget.PropagationCost)
        {
            deltas.Add($"propagation cost +{Percent(current.PropagationCost - budget.PropagationCost)}");
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
        if (budget.PropagationCost - current.PropagationCost >= ComplexityBudget.NoticeablePropagationDelta)
        {
            progress.Add($"propagation cost -{Percent(budget.PropagationCost - current.PropagationCost)}");
        }

        if (current.CoreSize < budget.CoreSize)
        {
            progress.Add($"core size -{budget.CoreSize - current.CoreSize} files");
        }

        return progress.Count == 0
            ? null
            : $"DSM complexity improved ({string.Join(", ", progress)}); run `harness budget update` to record the progress.";
    }

    private static string Percent(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + "%";
}

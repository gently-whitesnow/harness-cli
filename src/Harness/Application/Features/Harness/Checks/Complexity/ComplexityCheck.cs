using System.Globalization;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed class ComplexityCheck(ILanguageAnalyzer analyzer)
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

        if (graph.SourcePaths.Count == 0)
        {
            return CheckEvaluation.NotApplicable(Analyzer.NothingToAnalyze);
        }

        var metric = RepositoryComplexity.Measure(graph);
        var possiblePairs = (long)metric.AuthoredFiles * metric.AuthoredFiles;
        var observations = new[]
        {
            $"propagation cost: {Percent(metric.PropagationCostPercentage)} "
                + $"({metric.ReachablePairs} reachable file pairs / {possiblePairs})",
            $"core size: {metric.CoreFiles} files "
                + $"({Percent(metric.CorePercentage)} of {metric.AuthoredFiles} authored files)",
        };

        var (budget, budgetFailure) = ComplexityBudget.Load(context, BudgetEvidence);
        if (budget is null)
        {
            return CheckEvaluation.Incomplete(budgetFailure!, observations);
        }

        var current = ComplexityBudget.From(metric);
        if (current.PropagationCost > budget.PropagationCost || current.CoreSize > budget.CoreSize)
        {
            var findings = RegressionFindings(graph, budget, current);
            var regression = new Finding(
                FindingSeverity.Blocking,
                ComplexityBudget.FileName,
                RegressionMessage(budget, current));
            return CheckEvaluation.From(
                [regression, .. findings],
                detailedFindings: [regression, .. findings],
                observations: observations);
        }

        var progress = ProgressMessage(budget, current);
        return CheckEvaluation.From(
            [],
            observations: progress is null ? observations : [.. observations, progress]);
    }

    private static List<Finding> RegressionFindings(
        SourceGraph graph,
        ComplexityBudget budget,
        ComplexityBudget current)
    {
        var findings = new List<Finding>();
        if (current.PropagationCost > budget.PropagationCost)
        {
            findings.AddRange(graph.Proven
                .Where(edge => edge.From.Path != edge.To.Path)
                .GroupBy(edge => (edge.From.Path, edge.To.Path))
                .Select(group => group.First())
                .OrderBy(edge => edge.Location, StringComparer.Ordinal)
                .Take(5)
                .Select(edge => new Finding(
                    FindingSeverity.Blocking,
                    edge.Location,
                    $"Proven file edge {edge.From.Path} -> {edge.To.Path} contributes to propagation.")));
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

    private static string RegressionMessage(ComplexityBudget budget, ComplexityBudget current)
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

    private static string? ProgressMessage(ComplexityBudget budget, ComplexityBudget current)
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

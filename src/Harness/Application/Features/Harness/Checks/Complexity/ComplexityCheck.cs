using System.Globalization;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal sealed class ComplexityCheck(ILanguageAnalyzer analyzer)
    : LanguageAnalyzerCheck(analyzer, "complexity", "repository DSM complexity")
{
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
        return CheckEvaluation.From(
            [],
            observations:
            [
                $"propagation cost: {Percent(metric.PropagationCostPercentage)} "
                    + $"({metric.ReachablePairs} reachable file pairs / {possiblePairs})",
                $"core size: {metric.CoreFiles} files "
                    + $"({Percent(metric.CorePercentage)} of {metric.AuthoredFiles} authored files)",
            ]);
    }

    private static string Percent(double value)
        => value.ToString("F2", CultureInfo.InvariantCulture) + "%";
}

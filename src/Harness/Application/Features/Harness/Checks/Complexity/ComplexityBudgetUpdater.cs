using Harness.Git;
using Harness.Languages;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal static class ComplexityBudgetUpdater
{
    public static ComplexityBudgetUpdate Update(GitRepository repository, ILanguageAnalyzer analyzer)
    {
        var (graph, graphFailure) = analyzer.ReadGraph(repository);
        if (graph is null)
        {
            return new(ExitCodes.Incomplete, graphFailure!);
        }

        if (graph.SourcePaths.Count == 0)
        {
            return new(ExitCodes.Incomplete, analyzer.NothingToAnalyze);
        }

        var current = ComplexityBudget.From(RepositoryComplexity.Measure(graph));
        var path = Path.Combine(repository.RootPath, ComplexityBudget.FileName);
        var (existing, readFailure) = ComplexityBudget.LoadWorking(path);
        if (readFailure is not null)
        {
            return new(ExitCodes.Incomplete, readFailure);
        }

        if (existing is not null
            && (current.PropagationCost > existing.PropagationCost || current.CoreSize > existing.CoreSize))
        {
            return new(
                ExitCodes.Violation,
                $"REFUSED  current DSM metrics exceed the tracked budget; '{ComplexityBudget.FileName}' was not changed.");
        }

        if (existing == current)
        {
            return new(ExitCodes.Success, $"UNCHANGED  '{ComplexityBudget.FileName}' already matches current DSM metrics.");
        }

        var temporary = path + "." + Guid.NewGuid().ToString("N") + ".tmp";
        try
        {
            File.WriteAllText(temporary, current.Serialize());
            File.Move(temporary, path, overwrite: true);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            try
            {
                File.Delete(temporary);
            }
            catch (Exception cleanupException) when (cleanupException is IOException or UnauthorizedAccessException)
            {
            }

            return new(ExitCodes.Incomplete, $"Could not write '{ComplexityBudget.FileName}': {exception.Message}");
        }

        var action = existing is null ? "CREATED" : "UPDATED";
        return new(
            ExitCodes.Success,
            $"{action}  '{ComplexityBudget.FileName}' at propagation cost {current.PropagationCost:F6}% and core size {current.CoreSize} files.");
    }
}

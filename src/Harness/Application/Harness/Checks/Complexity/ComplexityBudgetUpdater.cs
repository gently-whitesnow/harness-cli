using Harness.Languages;
using Harness.Repository;
using Harness.Structure;

namespace Harness.Checks.Complexity;

internal static class ComplexityBudgetUpdater
{
    public static (string? Content, string? Failure) InitialContent(
        IRepository repository,
        IReadOnlyList<ILanguageAnalyzer> analyzers,
        bool architectureApplicable)
    {
        var (entries, failure) = Measure(repository, analyzers, architectureApplicable, allowEmpty: true);
        return entries is null
            ? (null, failure)
            : (new ComplexityBudget(entries).Serialize(), null);
    }

    public static ComplexityBudgetUpdate Update(
        IRepository repository,
        IReadOnlyList<ILanguageAnalyzer> analyzers,
        bool architectureApplicable)
    {
        var (entries, measureFailure) = Measure(repository, analyzers, architectureApplicable, allowEmpty: false);
        if (entries is null)
        {
            return new(ExitCodes.Incomplete, measureFailure!);
        }

        var expectedIds = entries.Keys.Order(StringComparer.Ordinal).ToList();
        var current = new ComplexityBudget(entries);
        var path = Path.Combine(repository.RootPath, ComplexityBudget.FileName);
        var legacy = ComplexityBudget.IsLegacy(path);
        var (existing, readFailure) = legacy ? (null, null) : ComplexityBudget.LoadWorking(path, expectedIds);
        if (readFailure is not null)
        {
            return new(ExitCodes.Incomplete, readFailure);
        }

        if (existing is not null && entries.Any(entry =>
            entry.Value.MeanReach > existing.Entries[entry.Key].MeanReach
            || entry.Value.CoreSize > existing.Entries[entry.Key].CoreSize))
        {
            return new(
                ExitCodes.Violation,
                $"REFUSED  current DSM metrics exceed the tracked budget; '{ComplexityBudget.FileName}' was not changed.");
        }

        if (existing is not null && entries.All(entry => entry.Value == existing.Entries[entry.Key]))
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

        var action = legacy ? "MIGRATED" : existing is null ? "CREATED" : "UPDATED";
        var metrics = string.Join(
            "; ",
            entries.OrderBy(entry => entry.Key, StringComparer.Ordinal).Select(entry =>
                $"{entry.Key}: mean reach {entry.Value.MeanReach:F6} files "
                    + $"and core size {entry.Value.CoreSize} files"));
        return new(
            ExitCodes.Success,
            $"{action}  '{ComplexityBudget.FileName}' at {metrics}.");
    }

    private static (Dictionary<string, ComplexityBudget.Entry>? Entries, string? Failure) Measure(
        IRepository repository,
        IReadOnlyList<ILanguageAnalyzer> analyzers,
        bool architectureApplicable,
        bool allowEmpty)
    {
        var (projects, projectFailure) = DotNetRepository.ReadProjects(repository);
        if (projectFailure is not null)
        {
            return (null, projectFailure);
        }

        var entries = new Dictionary<string, ComplexityBudget.Entry>(StringComparer.Ordinal);
        foreach (var analyzer in analyzers)
        {
            var (graph, graphFailure) = analyzer.ReadGraph(repository);
            if (graph is null)
            {
                return (null, graphFailure);
            }

            if (!allowEmpty && graph.SourcePaths.Count == 0)
            {
                return (null, analyzer.NothingToAnalyze);
            }

            var scope = DsmScope.Of(
                graph,
                architectureApplicable,
                repository.TrackedEntries.Where(entry => !entry.IsSymbolicLink).Select(entry => entry.Path).ToList(),
                projects.Select(project => (project.Path, DotNetRepository.IsTestProject(project))).ToList());
            entries[analyzer.Language.Qualify("complexity")] =
                ComplexityBudget.Entry.From(RepositoryComplexity.Measure(scope.Graph));
        }

        return (entries, null);
    }
}

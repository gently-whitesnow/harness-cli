using Harness.Checks;
using Harness.Config;
using Harness.Repository;

namespace Harness.Engine;

/// <summary>Validates the entire explicit workspace before executing independently scoped frames.</summary>
internal static class WorkspaceEngine
{
    public static WorkspaceReport Run(
        IRepository repository,
        IReadOnlyList<string> only,
        IReadOnlyList<string> skip,
        IReadOnlyList<IRepositoryCheck> checks,
        string? project = null)
    {
        var descriptors = CheckCatalog.Describe(checks);
        var (root, _) = HarnessConfigReader.Load(repository, descriptors);
        if (root is null)
        {
            return new WorkspaceReport(
                [new ScopedRunReport(".", HarnessConfig.FileName, GateEngine.Run(repository, only, skip, checks))],
                [], false, project is not null);
        }

        var errors = new List<string>();
        if (root.Projects.Count > 0)
        {
            AddFrameFailures(root, HarnessConfig.FileName, errors);
        }

        var registered = root.Projects.Select(path => path + "/" + HarnessConfig.FileName).ToHashSet(StringComparer.Ordinal);
        foreach (var entry in repository.TrackedEntries.Where(entry => entry.Path.EndsWith("/" + HarnessConfig.FileName, StringComparison.Ordinal)))
        {
            if (!registered.Contains(entry.Path))
            {
                errors.Add($"'{entry.Path}' is an unregistered project config; register its directory in root 'projects' or remove it");
            }
        }

        if (project is not null && !root.Projects.Contains(project, StringComparer.Ordinal))
        {
            errors.Add($"Unknown project '{project}'; choose a directory registered in root 'projects'");
        }

        var projects = new List<(string Path, IRepository Repository, HarnessConfig Config)>();
        foreach (var path in root.Projects)
        {
            var scoped = new ScopedRepository(repository, path);
            var (config, configFailure) = HarnessConfigReader.Load(scoped, descriptors, root);
            if (config is null)
            {
                var missing = scoped.TrackedEntries.All(entry => entry.Path != HarnessConfig.FileName)
                    ? " Project config is not in the index."
                    : string.Empty;
                errors.Add($"{path}/{HarnessConfig.FileName}: {configFailure}{missing}");
                continue;
            }

            AddFrameFailures(config, path + "/" + HarnessConfig.FileName, errors);

            projects.Add((path, scoped, config));
        }

        if (errors.Count > 0)
        {
            return new WorkspaceReport([], errors, root.Projects.Count > 0, project is not null);
        }

        var rootRepository = root.Projects.Count == 0 ? repository : new ScopedRepository(repository, string.Empty, root.Projects);
        var runs = new List<ScopedRunReport>
        {
            new(".", HarnessConfig.FileName, GateEngine.Run(rootRepository, only, skip, checks, root)),
        };
        var projectChecks = checks.Where(check => check.Id != "commits.setup").ToList();
        var projectOnly = only.Where(selector => selector is not ("commits.setup" or "commits")).ToList();
        var projectSkip = skip.Where(selector => selector is not ("commits.setup" or "commits")).ToList();
        if (only.Count == 0 || projectOnly.Count > 0)
        {
            foreach (var selected in projects.Where(candidate => project is null || candidate.Path == project))
            {
                runs.Add(new ScopedRunReport(selected.Path, selected.Path + "/" + HarnessConfig.FileName,
                    GateEngine.Run(selected.Repository, projectOnly, projectSkip, projectChecks, selected.Config)));
            }
        }

        return new WorkspaceReport(runs, [], root.Projects.Count > 0, project is not null);
    }

    private static void AddFrameFailures(HarnessConfig config, string path, List<string> errors)
    {
        foreach (var answerFailure in config.AnswerFailures)
        {
            errors.Add($"{path}: {answerFailure.Key}: {answerFailure.Value}");
        }

        if (config.ArchitectureFailure is not null)
        {
            errors.Add($"{path}: {config.ArchitectureFailure}");
        }
    }

}

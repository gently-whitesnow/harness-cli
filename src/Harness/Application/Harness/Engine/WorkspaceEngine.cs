using Harness.Checks;
using Harness.Config;
using Harness.Repository;

namespace Harness.Engine;

/// <summary>
/// Validates the entire explicit workspace before executing independently scoped frames. A
/// workspace that does not validate is reported as the root frame stopped at `harness.config`,
/// the same shape a repository without a readable frame gets.
/// </summary>
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
            var notes = project is null
                ? []
                : new[] { $"Project '{project}' was not verified: the root {HarnessConfig.FileName} could not be read." };
            return new WorkspaceReport(
                [new ScopedRunReport(".", HarnessConfig.FileName, GateEngine.Run(repository, only, skip, checks))],
                IsWorkspace: false,
                IsPartial: project is not null,
                notes);
        }

        var isWorkspace = root.Projects.Count > 0;
        if (project is not null && !root.Projects.Contains(project, StringComparer.Ordinal))
        {
            var hint = isWorkspace
                ? $"choose one of {string.Join(", ", root.Projects)}"
                : $"the root {HarnessConfig.FileName} registers no projects";
            return new WorkspaceReport(
                [new ScopedRunReport(".", HarnessConfig.FileName, GateEngine.Refused($"Unknown project '{project}'; {hint}."))],
                isWorkspace,
                IsPartial: true,
                []);
        }

        var errors = new List<string>();
        if (isWorkspace)
        {
            AddFrameFailures(root, HarnessConfig.FileName, errors);
        }

        errors.AddRange(WorkspaceScope.Unregistered(repository, root.Projects)
            .Select(path => $"'{path}' is an unregistered project config; register its directory in root 'projects' or remove it"));

        var projects = new List<(string Path, IRepository Repository, HarnessConfig Config)>();
        foreach (var path in root.Projects)
        {
            var scoped = new ScopedRepository(repository, path);
            var (config, configFailure) = HarnessConfigReader.Load(scoped, descriptors, root);
            if (config is null)
            {
                errors.Add($"{WorkspaceScope.ConfigPath(path)}: {configFailure}{Unstaged(scoped)}");
                continue;
            }

            AddFrameFailures(config, WorkspaceScope.ConfigPath(path), errors);
            projects.Add((path, scoped, config));
        }

        if (errors.Count > 0)
        {
            var failure = "The workspace does not validate; nothing was measured:\n  "
                + string.Join("\n  ", errors);
            return new WorkspaceReport(
                [new ScopedRunReport(".", HarnessConfig.FileName, GateEngine.Incomplete(repository, failure, checks))],
                isWorkspace,
                IsPartial: project is not null,
                []);
        }

        var runs = new List<ScopedRunReport>
        {
            new(".", HarnessConfig.FileName, GateEngine.Run(WorkspaceScope.RootScope(repository, root.Projects), only, skip, checks, root)),
        };

        // The commit integration belongs to the clone, so its check and group are the root's
        // alone; a selection naming only them leaves every project frame untouched.
        var projectOnly = only.Where(selector => !WorkspaceScope.IsRootOnlySelector(selector)).ToList();
        var projectSkip = skip.Where(selector => !WorkspaceScope.IsRootOnlySelector(selector)).ToList();
        var projectChecks = checks.Where(check => WorkspaceScope.IsProjectCheck(check.Id)).ToList();
        var selectionCoversProjects = only.Count == 0 || projectOnly.Count > 0;
        var selected = projects.Where(candidate => project is null || candidate.Path == project).ToList();
        if (selectionCoversProjects)
        {
            foreach (var (path, scoped, config) in selected)
            {
                runs.Add(new ScopedRunReport(path, WorkspaceScope.ConfigPath(path),
                    GateEngine.Run(scoped, projectOnly, projectSkip, projectChecks, config)));
            }
        }

        var partialNotes = project is null
            ? []
            : selectionCoversProjects
                ? new[] { $"Partial run: root checks and project '{project}' only; other projects were not measured." }
                : [$"Project '{project}' was not run: every selected check belongs to the root frame."];
        return new WorkspaceReport(runs, isWorkspace, project is not null, partialNotes);
    }

    // "Not in the index" is reserved for a file Git can see in the working tree (ADR-0026); a
    // project directory with no config at all is told to write one.
    private static string Unstaged(ScopedRepository scoped)
    {
        var (untracked, _) = scoped.ReadUntrackedPaths();
        return untracked is not null && untracked.Contains(HarnessConfig.FileName, StringComparer.Ordinal)
            ? " The file exists in the working tree but is not in the index; run `git add` on it."
            : string.Empty;
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

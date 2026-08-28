using Harness.Checks;
using Harness.Checks.Complexity;
using Harness.Cli;
using Harness.Commits;
using Harness.Config;
using Harness.Engine;
using Harness.Git;
using Harness.Host;
using Harness.Report;
using Harness.Repository;
using Harness.Versioning;

var invocation = Invocation.Parse(args, Directory.GetCurrentDirectory());
var checks = CheckRegistry.All;

switch (invocation.Kind)
{
    case CommandKind.Check:
    {
        var (repository, openFailure) = GitRepository.Open(invocation.RepositoryPath);
        if (repository is null)
        {
            var incomplete = new RunReport(invocation.RepositoryPath, [], openFailure);
            Console.Error.Write(ConsoleReport.Render(
                incomplete,
                invocation.Verbose,
                invocation.Only.Count > 0,
                invocation.All));
            return incomplete.ExitCode;
        }

        var report = GateEngine.Run(repository, invocation.Only, invocation.Skip, checks);
        var writer = report.ExitCode == ExitCodes.Incomplete ? Console.Error : Console.Out;
        writer.Write(ConsoleReport.Render(report, invocation.Verbose, invocation.Only.Count > 0, invocation.All));
        return report.ExitCode;
    }

    case CommandKind.Init:
    {
        var (repositoryKind, interviewFailure) = invocation.RepositoryKind is { } selected
            ? (selected, null)
            : ArchitectureInterview.Ask(Console.In, Console.Out);
        if (repositoryKind is null)
        {
            Console.Error.WriteLine(interviewFailure);
            return ExitCodes.Incomplete;
        }

        var (initRepository, initOpenFailure) = GitRepository.Open(invocation.RepositoryPath);
        if (initRepository is null)
        {
            Console.Error.WriteLine(initOpenFailure);
            return ExitCodes.Incomplete;
        }

        var (initialBudget, budgetFailure) =
            ComplexityBudgetUpdater.InitialContent(initRepository, CheckRegistry.LanguageAnalyzers);
        if (initialBudget is null)
        {
            Console.Error.WriteLine(budgetFailure);
            return ExitCodes.Incomplete;
        }

        var result = ConfigInitializer.Create(
            initRepository,
            invocation.Latest,
            invocation.CommitLanguage,
            repositoryKind.Value,
            CheckCatalog.Describe(checks),
            initialBudget);
        if (result.Failure is not null)
        {
            Console.Error.WriteLine(result.Failure);
            return ExitCodes.Incomplete;
        }

        Console.WriteLine(
            $"Created '{result.Path}' and "
            + $"'{Path.Combine(Path.GetDirectoryName(result.Path)!, ".harness.budget.json")}' "
            + "with the current tracked DSM metrics.");
        var commitSettings = new CommitSettings(invocation.CommitLanguage, RequireSetup: true);
        var (setup, setupFailure) = CheckRegistry.CommitIntegration.Install(
            initRepository,
            commitSettings,
            CommitTemplate.Render(commitSettings));
        if (setup is null)
        {
            Console.Error.WriteLine(setupFailure);
            Console.Error.WriteLine("The frame was created, but commit integration was not installed; run `harness setup`.");
            return ExitCodes.Incomplete;
        }

        Console.WriteLine("Configured the commit template and commit-msg hook for this clone.");
        Console.WriteLine(
            "Review every answer; ask the repository owner when intent is unclear rather than guessing.");
        Console.WriteLine("Track the file, then run `harness check --verbose`.");
        return ExitCodes.Success;
    }

    case CommandKind.Upgrade:
    {
        var (repository, openFailure) = GitRepository.Open(invocation.RepositoryPath);
        if (repository is null)
        {
            Console.Error.WriteLine(openFailure);
            return ExitCodes.Incomplete;
        }

        var (report, upgradeFailure) = FrameUpgrade.Raise(repository, invocation.DryRun);
        if (report is null)
        {
            Console.Error.WriteLine(upgradeFailure);
            return ExitCodes.Incomplete;
        }

        Console.Write(report);
        return ExitCodes.Success;
    }

    case CommandKind.BudgetUpdate:
    {
        var (repository, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (repository is null || config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        var analyzers = CheckRegistry.LanguageAnalyzers
            .Where(analyzer => config.NotApplicable(analyzer.Language.Key) is null)
            .ToList();
        if (analyzers.Count == 0)
        {
            Console.Error.WriteLine("Cannot update the DSM budget: no registered language is applicable.");
            return ExitCodes.Incomplete;
        }

        var result = ComplexityBudgetUpdater.Update(repository, analyzers);
        var writer = result.ExitCode == ExitCodes.Success ? Console.Out : Console.Error;
        writer.WriteLine(result.Message);
        return result.ExitCode;
    }

    case CommandKind.Setup:
    {
        var (repository, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (repository is null || config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        var (status, setupFailure) = CheckRegistry.CommitIntegration.Install(
            repository,
            config.Settings.Commits,
            CommitTemplate.Render(config.Settings.Commits));
        if (status is null)
        {
            Console.Error.WriteLine(setupFailure);
            return ExitCodes.Incomplete;
        }

        Console.WriteLine($"READY  {status.Description}.");
        return ExitCodes.Success;
    }

    case CommandKind.CommitTemplate:
    {
        var (_, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        Console.Write(CommitTemplate.Render(config.Settings.Commits));
        return ExitCodes.Success;
    }

    case CommandKind.CommitMessageCheck:
    {
        var (_, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        string message;
        try
        {
            message = File.ReadAllText(invocation.Operand!);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            Console.Error.WriteLine($"Could not read commit message '{invocation.Operand}': {exception.Message}");
            return ExitCodes.Incomplete;
        }

        var report = CommitMessageValidator.Validate(message, config.Settings.Commits, invocation.AllowFixup);
        PrintCommitReport(report);
        return report.Passed ? ExitCodes.Success : ExitCodes.Violation;
    }

    case CommandKind.CommitsCheck:
    {
        var (repository, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (repository is null || config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        var (commits, commitFailure) = repository.ReadCommits(invocation.Operand!);
        if (commits is null)
        {
            Console.Error.WriteLine(commitFailure);
            return ExitCodes.Incomplete;
        }

        if (commits.Count == 0)
        {
            Console.Error.WriteLine($"Commit range '{invocation.Operand}' is empty; nothing was verified.");
            return ExitCodes.Incomplete;
        }

        var failed = false;
        foreach (var (objectId, message) in commits)
        {
            var report = CommitMessageValidator.Validate(message, config.Settings.Commits, allowFixup: false);
            if (!report.Passed || report.Warnings.Count > 0)
            {
                Console.WriteLine($"{objectId[..12]}  {message.Split('\n')[0]}");
                PrintCommitReport(report);
            }

            failed |= !report.Passed;
        }

        if (!failed)
        {
            Console.WriteLine($"PASS  {commits.Count} commit{(commits.Count == 1 ? "" : "s")} verified.");
        }

        return failed ? ExitCodes.Violation : ExitCodes.Success;
    }

    case CommandKind.Explain:
    {
        var check = checks.FirstOrDefault(candidate => candidate.Id == invocation.CheckId);
        if (check is null)
        {
            Console.Error.WriteLine(
                $"Unknown check identifier: {invocation.CheckId}. "
                + $"Known identifiers: {string.Join(", ", checks.Select(candidate => candidate.Id))}.");
            return ExitCodes.Incomplete;
        }

        Console.WriteLine($"{check.Id}  {check.Summary}  (group {check.Group})");
        Console.WriteLine();
        Console.WriteLine(check.Explanation);
        Console.WriteLine();
        Console.WriteLine("Named evidence");
        Console.WriteLine(check.Evidence.Count == 0
            ? "  none — this check reports no named file as missing."
            : "  " + string.Join(", ", check.Evidence.Select(file => file.Name))
                + "\n  A run says when a file with such a name is in the working tree but not in the index.");
        return ExitCodes.Success;
    }

    case CommandKind.Version:
        Console.WriteLine($"harness {HarnessVersion.Current}");
        Console.WriteLine(
            $"Runs contract {HarnessVersion.Current}; every other pin requires `harness upgrade`.");
        return ExitCodes.Success;

    case CommandKind.Help:
        Console.Write(UsageText.For(CheckCatalog.Summaries(checks)));
        return ExitCodes.Success;

    default:
        Console.Error.Write(UsageText.For(CheckCatalog.Summaries(checks)));
        if (invocation.Error is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(invocation.Error);
        }

        return ExitCodes.Incomplete;
}

static (IRepository? Repository, HarnessConfig? Config, string? Failure) LoadRepository(
    string path,
    IReadOnlyList<IRepositoryCheck> checks)
{
    var (repository, openFailure) = GitRepository.Open(path);
    if (repository is null)
    {
        return (null, null, openFailure);
    }

    var (config, configFailure) = HarnessConfig.Load(repository, CheckCatalog.Describe(checks));
    return config is null
        ? (repository, null, configFailure)
        : (repository, config, null);
}

static void PrintCommitReport(CommitMessageReport report)
{
    foreach (var error in report.Errors)
    {
        Console.Error.WriteLine("ERROR    " + error);
    }

    foreach (var warning in report.Warnings)
    {
        Console.WriteLine("WARNING  " + warning);
    }

    if (report.Passed && report.Warnings.Count == 0)
    {
        Console.WriteLine("PASS  commit message follows the repository contract.");
    }
}

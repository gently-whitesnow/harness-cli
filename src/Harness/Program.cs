using Harness.Checks;
using Harness.Cli;
using Harness.Commits;
using Harness.Config;
using Harness.Engine;
using Harness.Git;

var invocation = Invocation.Parse(args, Directory.GetCurrentDirectory());
var checks = CheckRegistry.All;

switch (invocation.Kind)
{
    case CommandKind.Check:
    {
        var report = GateEngine.Run(invocation.RepositoryPath, invocation.Only, invocation.Skip, checks);
        var writer = report.ExitCode == ExitCodes.Incomplete ? Console.Error : Console.Out;
        writer.Write(ConsoleReport.Render(report, invocation.Verbose, invocation.Only.Count > 0));
        return report.ExitCode;
    }

    case CommandKind.Init:
    {
        var result = ConfigInitializer.Create(
            invocation.RepositoryPath,
            invocation.Latest,
            invocation.CommitLanguage,
            checks);
        if (result.Failure is not null)
        {
            Console.Error.WriteLine(result.Failure);
            return ExitCodes.Incomplete;
        }

        Console.WriteLine($"Created '{result.Path}'.");
        var (repository, openFailure) = GitRepository.Open(invocation.RepositoryPath);
        if (repository is null)
        {
            Console.Error.WriteLine(openFailure);
            return ExitCodes.Incomplete;
        }

        var commitSettings = new CommitSettings(invocation.CommitLanguage, RequireSetup: true);
        var (setup, setupFailure) = CommitHookSetup.Install(repository, commitSettings);
        if (setup is null)
        {
            Console.Error.WriteLine(setupFailure);
            Console.Error.WriteLine("The frame was created, but commit integration was not installed; run `harness setup`.");
            return ExitCodes.Incomplete;
        }

        Console.WriteLine("Configured the commit template and commit-msg hook for this clone.");
        Console.WriteLine(
            "Review every answer; ask the repository owner when intent is unclear rather than suppressing the work.");
        Console.WriteLine("Track the file, then run `harness check --verbose`.");
        return ExitCodes.Success;
    }

    case CommandKind.Setup:
    {
        var (repository, config, failure) = LoadRepository(invocation.RepositoryPath, checks);
        if (repository is null || config is null)
        {
            Console.Error.WriteLine(failure);
            return ExitCodes.Incomplete;
        }

        var (status, setupFailure) = CommitHookSetup.Install(repository, config.Settings.Commits);
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
        return ExitCodes.Success;
    }

    case CommandKind.Help:
        Console.Write(UsageText.For(checks));
        return ExitCodes.Success;

    default:
        Console.Error.Write(UsageText.For(checks));
        if (invocation.Error is not null)
        {
            Console.Error.WriteLine();
            Console.Error.WriteLine(invocation.Error);
        }

        return ExitCodes.Incomplete;
}

static (GitRepository? Repository, HarnessConfig? Config, string? Failure) LoadRepository(
    string path,
    IReadOnlyList<IRepositoryCheck> checks)
{
    var (repository, openFailure) = GitRepository.Open(path);
    if (repository is null)
    {
        return (null, null, openFailure);
    }

    var (config, configFailure) = HarnessConfig.Load(repository, checks);
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

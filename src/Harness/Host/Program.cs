using Harness.Checks;
using Harness.Cli;
using Harness.Commits;
using Harness.Config;
using Harness.Engine;
using Harness.Git;
using Harness.Host;
using Harness.Languages;
using Harness.Report;
using Harness.Repository;
using Harness.Versioning;

var invocation = Invocation.Parse(args, Directory.GetCurrentDirectory());
var checks = CheckRegistry.All;

switch (invocation.Kind)
{
    case CommandKind.Check:
        return ExecuteCheck(invocation, checks);

    case CommandKind.Init:
        return ExecuteInit(invocation, checks);

    case CommandKind.Upgrade:
        return ExecuteUpgrade(invocation, checks);

    case CommandKind.Setup:
        return ExecuteSetup(invocation, checks);

    case CommandKind.CommitTemplate:
        return ExecuteCommitTemplate(invocation, checks);

    case CommandKind.CommitMessageCheck:
        return ExecuteCommitMessageCheck(invocation, checks);

    case CommandKind.CommitsCheck:
        return ExecuteCommitsCheck(invocation, checks);

    case CommandKind.Explain:
        return ExecuteExplain(invocation, checks);

    case CommandKind.Guide:
        Console.Write(GuideText.For(HarnessVersion.Current.ToString()));
        return ExitCodes.Success;

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

    var (config, configFailure) = HarnessConfigReader.Load(repository, CheckCatalog.Describe(checks));
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


static int ExecuteCheck(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
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

    var report = WorkspaceEngine.Run(repository, invocation.Only, invocation.Skip, checks, invocation.Project);
    var writer = report.ExitCode == ExitCodes.Incomplete ? Console.Error : Console.Out;
    writer.Write(WorkspaceConsoleReport.Render(report, invocation.Verbose, invocation.Only.Count > 0, invocation.All));
    return report.ExitCode;
}

static int ExecuteInit(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
{
    var (initRepository, initOpenFailure) = GitRepository.Open(invocation.RepositoryPath);
    if (initRepository is null)
    {
        Console.Error.WriteLine(initOpenFailure);
        return ExitCodes.Incomplete;
    }

    // The frame names only the stacks the index shows, or the ones CI names explicitly.
    List<FrameAxis> axes;
    if (invocation.Languages is null)
    {
        axes = FrameAxis.Detected(initRepository);
    }
    else
    {
        var unknown = invocation.Languages.Where(key => FrameAxis.Named(key) is null).ToList();
        if (unknown.Count > 0)
        {
            Console.Error.WriteLine($"--languages names no applicability this harness ships: {string.Join(", ", unknown)}. "
                + $"Known keys: {string.Join(", ", FrameAxis.All.Select(axis => axis.Key))}.");
            return ExitCodes.Incomplete;
        }

        axes = FrameAxis.All.Where(axis => invocation.Languages.Contains(axis.Key, StringComparer.Ordinal)).ToList();
    }

    // sliced-dotnet/1 shapes C# code, so the kind question has a subject only with C# in the index.
    var repositoryKind = invocation.RepositoryKind;
    if (repositoryKind is null && ConfigInitializer.AsksArchitecture(axes))
    {
        var (asked, interviewFailure) = ArchitectureInterview.Ask(Console.In, Console.Out);
        if (asked is null)
        {
            Console.Error.WriteLine(interviewFailure);
            return ExitCodes.Incomplete;
        }

        repositoryKind = asked;
    }

    var result = ConfigInitializer.Create(
        initRepository,
        invocation.Latest,
        invocation.CommitLanguage,
        repositoryKind,
        axes,
        CheckCatalog.Describe(checks));
    if (result.Failure is not null)
    {
        Console.Error.WriteLine(result.Failure);
        return ExitCodes.Incomplete;
    }

    PrintInitSummary(invocation, axes, result.Path!, result.EditorConfigPath);
    var commitSettings = new CommitSettings(invocation.CommitLanguage, RequireSetup: true);
    var (setup, setupFailure) = CheckRegistry.CommitIntegration.Install(
        initRepository,
        commitSettings,
        CommitTemplate.Render(commitSettings),
        invocation.Latest ? null : HarnessVersion.Current);
    if (setup is null)
    {
        Console.Error.WriteLine(setupFailure);
        Console.Error.WriteLine("The frame was created, but commit integration was not installed; run `harness setup`.");
        return ExitCodes.Incomplete;
    }

    Console.WriteLine(setup.Ready
        ? "Configured the commit template and commit-msg hook for this clone."
        : $"Configured the commit template and commit-msg hook, but {setup.Description}.");
    Console.WriteLine(
        "Review every answer; ask the repository owner when intent is unclear rather than guessing.");
    Console.WriteLine("All initialized checks are required. Discuss each finding with the repository owner before changing policy to advisory or off.");
    Console.WriteLine("Track the file, then run `harness check --verbose`.");
    return ExitCodes.Success;
}

static int ExecuteUpgrade(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
{
    var (repository, openFailure) = GitRepository.Open(invocation.RepositoryPath);
    if (repository is null)
    {
        Console.Error.WriteLine(openFailure);
        return ExitCodes.Incomplete;
    }

    var (report, upgradeFailure) = FrameUpgrade.Raise(repository, invocation.DryRun, CheckCatalog.Describe(checks));
    if (report is null)
    {
        Console.Error.WriteLine(upgradeFailure);
        return ExitCodes.Incomplete;
    }

    Console.Write(report);
    return ExitCodes.Success;
}

static int ExecuteSetup(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
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
        CommitTemplate.Render(config.Settings.Commits),
        config.TracksLatest ? null : config.Version);
    if (status is null)
    {
        Console.Error.WriteLine(setupFailure);
        return ExitCodes.Incomplete;
    }

    // Setup writes the managed files, but the gate only holds when the hook can also find a
    // harness to run, so an unfinished clone says so instead of reporting a ready state.
    if (!status.Ready)
    {
        Console.Error.WriteLine($"The commit-msg hook and template are installed, but {status.Description}.");
        return ExitCodes.Incomplete;
    }

    Console.WriteLine($"READY  {status.Description}.");
    return ExitCodes.Success;
}

static int ExecuteCommitTemplate(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
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

static int ExecuteCommitMessageCheck(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
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

static int ExecuteCommitsCheck(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
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

static int ExecuteExplain(Invocation invocation, IReadOnlyList<IRepositoryCheck> checks)
{
    if (invocation.CheckId is "architecture" or "architecture.sliced-dotnet")
    {
        var members = checks.Where(candidate => candidate.Group == "architecture.sliced-dotnet").ToList();
        foreach (var member in members)
        {
            Console.WriteLine($"{member.Id}  {member.Summary}");
        }
        Console.WriteLine();
        Console.WriteLine(members[0].Explanation);
        return ExitCodes.Success;
    }
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

static void PrintInitSummary(Invocation invocation, IReadOnlyList<FrameAxis> axes, string path, string? editorConfigPath)
{
    Console.WriteLine($"Created '{path}'.");
    Console.WriteLine(axes.Count == 0
        ? "Declared no language or stack: the index shows no tracked sources the harness reads."
        : $"Declared {string.Join(", ", axes.Select(axis => axis.Key))} from the "
            + (invocation.Languages is null ? "tracked sources in the index." : "--languages option."));
    if (axes.Any(axis => axis.Key == FrameAxis.DotNet.Key))
    {
        Console.WriteLine(editorConfigPath is not null
            ? $"Created '{editorConfigPath}' with the shared code-style baseline."
            : "Kept the existing '.editorconfig'; `harness explain editorconfig.dotnet` prints the baseline it must carry.");
    }
    if (axes.Any(axis => axis.Key == Language.Go.Key))
    {
        Console.WriteLine("Go: answer `lint` with the place `go vet ./...` runs and `format` with the place "
            + "`gofmt -l` is checked (the verify script or a workflow); gofmt has no configuration to point at.");
    }
    if (axes.Any(axis => axis.Key == Language.Ansible.Key))
    {
        Console.WriteLine("Ansible: answer `lint` with the place `ansible-lint` and `yamllint` run, `build` with the place "
            + "`ansible-playbook --syntax-check` runs and `tests.integration` with the molecule scenarios "
            + "(a Makefile target, the verify script or a workflow); the harness runs none of them.");
    }
}

namespace Harness.Cli;

/// <summary>What the user asked the harness to do.</summary>
internal enum CommandKind
{
    Check,
    BudgetUpdate,
    Init,
    Upgrade,
    Setup,
    CommitMessageCheck,
    CommitTemplate,
    CommitsCheck,
    Explain,
    Version,
    Help,
    Usage,
}

/// <summary>
/// A parsed invocation. Parsing never fails silently: an unusable command line becomes
/// a <see cref="CommandKind.Usage"/> request carrying the reason.
/// </summary>
internal sealed record Invocation(CommandKind Kind, string RepositoryPath)
{
    public IReadOnlyList<string> Only { get; init; } = [];

    public IReadOnlyList<string> Skip { get; init; } = [];

    public bool Verbose { get; init; }

    public bool All { get; init; }

    public bool Latest { get; init; }

    public bool DryRun { get; init; }

    public string? CheckId { get; init; }

    public string? Error { get; init; }

    public string? Operand { get; init; }

    public bool AllowFixup { get; init; }

    public CommitLanguage CommitLanguage { get; init; } = CommitSettings.Default.Language;

    public static Invocation Parse(IReadOnlyList<string> arguments, string currentDirectory)
    {
        if (arguments.Count == 0)
        {
            return Usage(currentDirectory, error: null);
        }

        var command = arguments[0];
        var rest = arguments.Skip(1).ToList();

        return command switch
        {
            "check" => ParseCheck(rest, currentDirectory),
            "budget" => ParseBudget(rest, currentDirectory),
            "init" => ParseInit(rest, currentDirectory),
            "upgrade" => ParseUpgrade(rest, currentDirectory),
            "setup" => ParseSetup(rest, currentDirectory),
            "commit-message" => ParseCommitMessage(rest, currentDirectory),
            "commits" => ParseCommits(rest, currentDirectory),
            "explain" => ParseExplain(rest, currentDirectory),
            "version" or "--version" or "-v" => new Invocation(CommandKind.Version, currentDirectory),
            "help" or "--help" or "-h" => new Invocation(CommandKind.Help, currentDirectory),
            _ => Usage(currentDirectory, $"Unknown command '{command}'."),
        };
    }

    private static Invocation ParseUpgrade(List<string> arguments, string currentDirectory)
    {
        var dryRun = false;
        string? path = null;
        foreach (var argument in arguments)
        {
            if (argument == "--dry-run")
            {
                if (dryRun)
                {
                    return Usage(currentDirectory, "--dry-run may only be given once.");
                }

                dryRun = true;
            }
            else if (argument.StartsWith('-'))
            {
                return Usage(currentDirectory, $"Unknown option '{argument}'.");
            }
            else if (path is not null)
            {
                return Usage(currentDirectory, "Only one repository path may be given.");
            }
            else
            {
                path = argument;
            }
        }

        return new Invocation(
            CommandKind.Upgrade,
            Path.GetFullPath(path ?? currentDirectory, currentDirectory))
        {
            DryRun = dryRun,
        };
    }

    private static Invocation ParseCheck(List<string> arguments, string currentDirectory)
    {
        var only = new List<string>();
        var skip = new List<string>();
        var verbose = false;
        var all = false;
        string? path = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            switch (argument)
            {
                case "--only":
                case "--skip":
                    if (index + 1 >= arguments.Count)
                    {
                        return Usage(currentDirectory, $"{argument} requires a check identifier.");
                    }

                    var target = argument == "--only" ? only : skip;
                    target.AddRange(SplitIdentifiers(arguments[++index]));
                    break;

                case "--verbose":
                    verbose = true;
                    break;

                case "--all":
                    all = true;
                    verbose = true;
                    break;

                default:
                    if (argument.StartsWith('-'))
                    {
                        return Usage(currentDirectory, $"Unknown option '{argument}'.");
                    }

                    if (path is not null)
                    {
                        return Usage(currentDirectory, "Only one repository path may be given.");
                    }

                    path = argument;
                    break;
            }
        }

        var repositoryPath = Path.GetFullPath(path ?? currentDirectory, currentDirectory);
        return new Invocation(CommandKind.Check, repositoryPath)
        {
            Only = only,
            Skip = skip,
            Verbose = verbose,
            All = all,
        };
    }

    private static Invocation ParseInit(List<string> arguments, string currentDirectory)
    {
        var latest = false;
        var language = CommitSettings.Default.Language;
        var languageSeen = false;
        string? path = null;

        for (var index = 0; index < arguments.Count; index++)
        {
            var argument = arguments[index];
            if (argument == "--latest")
            {
                if (latest)
                {
                    return Usage(currentDirectory, "--latest may only be given once.");
                }

                latest = true;
                continue;
            }

            if (argument == "--language")
            {
                if (languageSeen || index + 1 >= arguments.Count)
                {
                    return Usage(currentDirectory, "--language requires one value and may only be given once.");
                }

                languageSeen = true;
                language = arguments[++index] switch
                {
                    "en" => CommitLanguage.English,
                    "ru" => CommitLanguage.Russian,
                    _ => (CommitLanguage)(-1),
                };
                if ((int)language < 0)
                {
                    return Usage(currentDirectory, "--language must be 'en' or 'ru'.");
                }

                continue;
            }

            if (argument.StartsWith('-'))
            {
                return Usage(currentDirectory, $"Unknown option '{argument}'.");
            }

            if (path is not null)
            {
                return Usage(currentDirectory, "Only one repository path may be given.");
            }

            path = argument;
        }

        var repositoryPath = Path.GetFullPath(path ?? currentDirectory, currentDirectory);
        return new Invocation(CommandKind.Init, repositoryPath)
        {
            Latest = latest,
            CommitLanguage = language,
        };
    }

    private static Invocation ParseBudget(List<string> arguments, string currentDirectory)
    {
        if (arguments.Count is < 1 or > 2 || arguments[0] != "update"
            || arguments.Skip(1).Any(argument => argument.StartsWith('-')))
        {
            return Usage(currentDirectory, "budget requires `update [path]`.");
        }

        var repositoryPath = Path.GetFullPath(arguments.ElementAtOrDefault(1) ?? currentDirectory, currentDirectory);
        return new Invocation(CommandKind.BudgetUpdate, repositoryPath);
    }

    private static Invocation ParseSetup(List<string> arguments, string currentDirectory)
    {
        if (arguments.Count > 1 || arguments.Any(argument => argument.StartsWith('-')))
        {
            return Usage(currentDirectory, "setup accepts at most one repository path.");
        }

        var repositoryPath = Path.GetFullPath(arguments.FirstOrDefault() ?? currentDirectory, currentDirectory);
        return new Invocation(CommandKind.Setup, repositoryPath);
    }

    private static Invocation ParseCommitMessage(List<string> arguments, string currentDirectory)
    {
        if (arguments.Count == 1 && arguments[0] == "template")
        {
            return new Invocation(CommandKind.CommitTemplate, currentDirectory);
        }

        if (arguments.Count < 2 || arguments[0] != "check")
        {
            return Usage(currentDirectory, "commit-message requires `check <message-file>` or `template`.");
        }

        var allowFixup = false;
        string? messagePath = null;
        foreach (var argument in arguments.Skip(1))
        {
            if (argument == "--allow-fixup")
            {
                if (allowFixup)
                {
                    return Usage(currentDirectory, "--allow-fixup may only be given once.");
                }

                allowFixup = true;
            }
            else if (argument.StartsWith('-') || messagePath is not null)
            {
                return Usage(currentDirectory, "commit-message check accepts one message file.");
            }
            else
            {
                messagePath = Path.GetFullPath(argument, currentDirectory);
            }
        }

        return messagePath is null
            ? Usage(currentDirectory, "commit-message check requires a message file.")
            : new Invocation(CommandKind.CommitMessageCheck, currentDirectory)
            {
                Operand = messagePath,
                AllowFixup = allowFixup,
            };
    }

    private static Invocation ParseCommits(List<string> arguments, string currentDirectory)
        => arguments.Count == 2 && arguments[0] == "check" && !arguments[1].StartsWith('-')
            ? new Invocation(CommandKind.CommitsCheck, currentDirectory) { Operand = arguments[1] }
            : Usage(currentDirectory, "commits requires `check <base>..<head>`.");

    private static Invocation ParseExplain(List<string> arguments, string currentDirectory)
    {
        if (arguments.Count != 1 || arguments[0].StartsWith('-'))
        {
            return Usage(currentDirectory, "explain requires exactly one check identifier.");
        }

        return new Invocation(CommandKind.Explain, currentDirectory) { CheckId = arguments[0] };
    }

    private static string[] SplitIdentifiers(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Invocation Usage(string currentDirectory, string? error)
        => new(CommandKind.Usage, currentDirectory) { Error = error };
}

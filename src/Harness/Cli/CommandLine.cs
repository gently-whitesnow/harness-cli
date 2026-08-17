namespace Harness.Cli;

/// <summary>What the user asked the harness to do.</summary>
internal enum CommandKind
{
    Check,
    Init,
    Explain,
    Help,
    Usage,
}

/// <summary>
/// A parsed invocation. Parsing never fails silently: an unusable command line becomes
/// a <see cref="CommandKind.Usage"/> request carrying the reason.
/// </summary>
internal sealed record Invocation(
    CommandKind Kind,
    string RepositoryPath,
    IReadOnlyList<string> Only,
    IReadOnlyList<string> Skip,
    bool Verbose,
    bool Latest,
    string? CheckId,
    string? Error)
{
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
            "init" => ParseInit(rest, currentDirectory),
            "explain" => ParseExplain(rest, currentDirectory),
            "help" or "--help" or "-h" => new Invocation(
                CommandKind.Help, currentDirectory, [], [], false, false, null, null),
            _ => Usage(currentDirectory, $"Unknown command '{command}'."),
        };
    }

    private static Invocation ParseCheck(List<string> arguments, string currentDirectory)
    {
        var only = new List<string>();
        var skip = new List<string>();
        var verbose = false;
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
        return new Invocation(CommandKind.Check, repositoryPath, only, skip, verbose, false, null, null);
    }

    private static Invocation ParseInit(List<string> arguments, string currentDirectory)
    {
        var latest = false;
        string? path = null;

        foreach (var argument in arguments)
        {
            if (argument == "--latest")
            {
                if (latest)
                {
                    return Usage(currentDirectory, "--latest may only be given once.");
                }

                latest = true;
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
        return new Invocation(CommandKind.Init, repositoryPath, [], [], false, latest, null, null);
    }

    private static Invocation ParseExplain(List<string> arguments, string currentDirectory)
    {
        if (arguments.Count != 1 || arguments[0].StartsWith('-'))
        {
            return Usage(currentDirectory, "explain requires exactly one check identifier.");
        }

        return new Invocation(CommandKind.Explain, currentDirectory, [], [], false, false, arguments[0], null);
    }

    private static IEnumerable<string> SplitIdentifiers(string value)
        => value.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

    private static Invocation Usage(string currentDirectory, string? error)
        => new(CommandKind.Usage, currentDirectory, [], [], false, false, null, error);
}

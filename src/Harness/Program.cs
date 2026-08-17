using Harness.Checks;
using Harness.Cli;
using Harness.Config;
using Harness.Engine;

var invocation = Invocation.Parse(args, Directory.GetCurrentDirectory());
IReadOnlyList<IRepositoryCheck> checks = CheckRegistry.All;

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
            checks);
        if (result.Failure is not null)
        {
            Console.Error.WriteLine(result.Failure);
            return ExitCodes.Incomplete;
        }

        Console.WriteLine($"Created '{result.Path}'.");
        Console.WriteLine(
            "Review every answer; ask the repository owner when intent is unclear rather than suppressing the work.");
        Console.WriteLine("Track the file, then run `harness check --verbose`.");
        return ExitCodes.Success;
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

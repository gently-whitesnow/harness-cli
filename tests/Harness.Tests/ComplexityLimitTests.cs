namespace Harness.Tests;

/// <summary>
/// The DSM limits are constants of the standard (ADR-0052): 8.0 files of mean reach and an
/// acyclic file graph, compared inside the binary with no tracked number to raise.
/// </summary>
public sealed class ComplexityLimitTests
{
    private const string Check = "complexity.csharp";

    [Fact]
    public void Mean_reach_above_eight_files_is_blocking_and_names_the_hubs()
    {
        using var repository = Graph(Chain(16));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("limits: mean reach 8.00 files · core size 0 files"), run.Output);
        Assert.True(run.OutputContains("mean reach 8.50 files exceeds the 8.00 files the standard allows"), run.Output);
        Assert.True(run.OutputContains("src/Graph/F01.cs: A change here reaches 16 of 16 files."), run.Output);
        Assert.False(run.OutputContains("budget"), run.Output);
    }

    [Fact]
    public void Mean_reach_of_exactly_eight_files_passes()
    {
        using var repository = Graph(Chain(15));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach: 8.00 files (120 reachable file pairs / 15 files"), run.Output);
        Assert.True(run.OutputContains("outcome: passed"), run.Output);
    }

    [Fact]
    public void A_two_file_cycle_is_blocking_and_names_both_files()
    {
        using var repository = Graph(("A", ["B"]), ("B", ["A"]));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("largest SCC (2 files); the standard allows 0"), run.Output);
        Assert.True(run.OutputContains("src/Graph/A.cs"), run.Output);
        Assert.True(run.OutputContains("src/Graph/B.cs"), run.Output);
    }

    [Fact]
    public void Hubs_are_named_outside_the_composition_root()
    {
        var repository = Fixtures.Compliant(Frame.AllPresent()
                .Architecture("""{ "standard": "sliced-dotnet/1" }"""))
            .WriteFile(
                "src/App/Host/Program.cs",
                "using App.Application.Example;\n\nnamespace App.Host;\n\nsealed class Program\n{\n    private F01? root;\n}\n");
        foreach (var (name, dependencies) in Chain(16))
        {
            repository.WriteFile(
                $"src/App/Application/Example/{name}.cs",
                Source("App.Application.Example", name, dependencies));
        }

        using var committed = repository.Commit();
        var run = HarnessCli.RunVerbose(committed.Path, "check", "--only", Check, "--all");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/Application/Example/F01.cs: A change here reaches 16 of 17 files."), run.Output);
        Assert.False(run.OutputContains("src/App/Host/Program.cs: A change here reaches"), run.Output);
    }

    [Fact]
    public void Advisory_policy_reports_the_excess_without_failing()
    {
        using var repository = Graph("advisory", Chain(16));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach 8.50 files exceeds the 8.00 files"), run.Output);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void Off_policy_does_not_run_the_check()
    {
        using var repository = Graph("off", Chain(16));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("turns this check off"), run.Output);
    }

    [Fact]
    public void A_tracked_budget_file_from_an_earlier_contract_is_incomplete_until_removed()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(".harness.budget.json", """{ "complexity.csharp": { "meanReach": 1.5, "coreSize": 0 } }""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("'.harness.budget.json' is tracked, but this contract keeps no DSM budget"), run.Output);
        Assert.True(run.OutputContains("git rm .harness.budget.json"), run.Output);
    }

    /// <summary>F01 → F02 → … → Fn: mean reach is (n + 1) / 2, so 15 files sit exactly on the limit.</summary>
    private static (string Name, string[] Dependencies)[] Chain(int length)
        => Enumerable.Range(1, length)
            .Select(index => ($"F{index:00}", index == length ? Array.Empty<string>() : [$"F{index + 1:00}"]))
            .ToArray();

    private static RepositoryFixture Graph(params (string Name, string[] Dependencies)[] files)
        => Graph("required", files);

    private static RepositoryFixture Graph(string policy, params (string Name, string[] Dependencies)[] files)
    {
        var repository = Fixtures.Compliant(Frame.AllPresent().Policy(Check, policy));
        foreach (var (name, dependencies) in files)
        {
            repository.WriteFile($"src/Graph/{name}.cs", Source("Graph", name, dependencies));
        }

        return repository.Commit();
    }

    private static string Source(string module, string name, string[] dependencies)
    {
        var fields = string.Join(
            "\n",
            dependencies.Select((dependency, index) => $"    private {dependency}? value{index};"));
        return $$"""
            namespace {{module}};

            public sealed class {{name}}
            {
            {{fields}}
            }

            """;
    }
}

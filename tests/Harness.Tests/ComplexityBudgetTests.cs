using System.Globalization;

namespace Harness.Tests;

public sealed class ComplexityBudgetTests
{
    private const string Check = "complexity.csharp";

    [Fact]
    public void Missing_tracked_budget_is_an_incomplete_configuration_with_initialization_hint()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .Remove(".harness.budget.json")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains(".harness.budget.json' is not tracked"), run.Output);
        Assert.True(run.OutputContains("harness budget update"), run.Output);
    }

    [Fact]
    public void Propagation_regression_is_blocking_and_names_delta_and_proven_edges()
    {
        using var repository = Graph(("A", ["B"]), ("B", ["C"]), ("C", []))
            .WriteFile(".harness.budget.json", Budget(1, 0))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("mean reach +1.00 files"), run.Output);
        Assert.True(run.OutputContains("Proven file edge src/Graph/A.cs -> src/Graph/B.cs"), run.Output);
    }

    [Fact]
    public void Propagation_regression_ranks_edges_by_structural_span_instead_of_path()
    {
        using var repository = Graph(
                ("A", ["B"]),
                ("B", []),
                ("M", ["N"]),
                ("N", ["O"]),
                ("O", []),
                ("P", ["M"]),
                ("Q", ["P"]))
            .WriteFile(".harness.budget.json", Budget(1, 0))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        var highImpact = run.Output.IndexOf("src/Graph/M.cs -> src/Graph/N.cs", StringComparison.Ordinal);
        var alphabeticalFirst = run.Output.IndexOf("src/Graph/A.cs -> src/Graph/B.cs", StringComparison.Ordinal);
        Assert.True(highImpact >= 0, run.Output);
        Assert.True(alphabeticalFirst > highImpact, run.Output);
        Assert.True(run.OutputContains("propagation span of 6 reachable file pairs"), run.Output);
    }

    [Fact]
    public void Core_regression_names_the_largest_strongly_connected_component()
    {
        using var repository = Graph(("A", ["B"]), ("B", ["C"]), ("C", ["A"]))
            .WriteFile(".harness.budget.json", Budget(100, 1))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("core size +2 files"), run.Output);
        Assert.True(run.OutputContains("largest SCC (3 files)"), run.Output);
        Assert.True(run.OutputContains("src/Graph/A.cs"), run.Output);
    }

    [Fact]
    public void Improvement_is_advisory_and_offers_to_record_progress()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(".harness.budget.json", Budget(100, 2))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("DSM complexity improved"), run.Output);
        Assert.True(run.OutputContains("harness budget update"), run.Output);
    }

    [Fact]
    public void Manually_raised_budget_keeps_the_check_green()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(".harness.budget.json", Budget(80, 0))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Sub_threshold_improvement_does_not_emit_an_advisory()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(".harness.budget.json", Budget(1.55, 0))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("DSM complexity improved"), run.Output);
    }

    [Fact]
    public void Budget_update_initializes_a_stable_diffable_file_from_current_metrics()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .Remove(".harness.budget.json")
            .Commit();

        var update = HarnessCli.Run(repository.Path, "budget", "update");
        var content = File.ReadAllText(Path.Combine(repository.Path, ".harness.budget.json"));

        Assert.Equal(0, update.ExitCode);
        Assert.Equal(Budget(1.5, 0), content);
        Assert.Equal(2, HarnessCli.Run(repository.Path, "check", "--only", Check).ExitCode);

        repository.Commit();
        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", Check).ExitCode);
        Assert.Equal("UNCHANGED", HarnessCli.Run(repository.Path, "budget", "update").StandardOutput.Split(' ')[0]);
    }

    [Fact]
    public void Budget_update_refuses_an_increase_and_keeps_the_file_unchanged()
    {
        using var repository = Graph(("A", []), ("B", []))
            .WriteFile(".harness.budget.json", Budget(1, 0))
            .Commit();
        repository.WriteFile(
            "src/Graph/A.cs",
            """
            namespace Graph;

            public sealed class A
            {
                private B? value;
            }

            """).Commit();
        var path = Path.Combine(repository.Path, ".harness.budget.json");
        var before = File.ReadAllText(path);

        var update = HarnessCli.Run(repository.Path, "budget", "update");

        Assert.Equal(1, update.ExitCode);
        Assert.True(update.OutputContains("REFUSED"), update.Output);
        Assert.Equal(before, File.ReadAllText(path));
    }

    [Fact]
    public void Budget_update_shrinks_an_existing_budget_to_current_metrics()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(".harness.budget.json", Budget(100, 2))
            .Commit();

        var update = HarnessCli.Run(repository.Path, "budget", "update");

        Assert.Equal(0, update.ExitCode);
        Assert.True(update.OutputContains("UPDATED"), update.Output);
        Assert.Equal(Budget(1.5, 0), File.ReadAllText(Path.Combine(repository.Path, ".harness.budget.json")));
    }

    [Fact]
    public void A_contract_2_5_budget_is_incomplete_until_budget_update_migrates_it()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(
                ".harness.budget.json",
                """
                {
                  "complexity.csharp": {
                    "propagationCost": 75,
                    "coreSize": 0
                  }
                }

                """)
            .Commit();

        var check = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);
        Assert.Equal(2, check.ExitCode);
        Assert.True(check.OutputContains("records propagationCost from contract 2.5"), check.Output);
        Assert.True(check.OutputContains("harness budget update"), check.Output);

        var update = HarnessCli.Run(repository.Path, "budget", "update");
        Assert.Equal(0, update.ExitCode);
        Assert.StartsWith("MIGRATED", update.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(Budget(1.5, 0), File.ReadAllText(Path.Combine(repository.Path, ".harness.budget.json")));

        repository.Commit();
        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", Check).ExitCode);
    }

    [Fact]
    public void Advisory_policy_keeps_the_regression_visible_without_failing()
    {
        using var repository = Graph("advisory", ("A", ["B"]), ("B", ["C"]), ("C", []))
            .WriteFile(".harness.budget.json", Budget(1, 0))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach +1.00 files"), run.Output);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void Off_policy_does_not_run_the_budget_check()
    {
        using var repository = Graph("off", ("A", ["B"]), ("B", ["C"]), ("C", []))
            .WriteFile(".harness.budget.json", Budget(1, 0))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("turns this check off"), run.Output);
    }

    private static RepositoryFixture Graph(params (string Name, string[] Dependencies)[] files)
        => Graph("required", files);

    private static RepositoryFixture Graph(string policy, params (string Name, string[] Dependencies)[] files)
    {
        var repository = Fixtures.Compliant(Frame.AllPresent().Policy(Check, policy));
        foreach (var (name, dependencies) in files)
        {
            var fields = string.Join(
                "\n",
                dependencies.Select((dependency, index) => $"    private {dependency}? value{index};"));
            repository.WriteFile(
                $"src/Graph/{name}.cs",
                $$"""
                namespace Graph;

                public sealed class {{name}}
                {
                {{fields}}
                }

                """);
        }

        return repository.Commit();
    }

    private static string Budget(double meanReach, int coreSize)
        => $$"""
        {
          "complexity.csharp": {
            "meanReach": {{meanReach.ToString(CultureInfo.InvariantCulture)}},
            "coreSize": {{coreSize}}
          }
        }

        """;
}

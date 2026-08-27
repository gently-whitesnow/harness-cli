namespace Harness.Tests;

public sealed class ComplexityTests
{
    private const string Check = "complexity.csharp";

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void A_three_file_tree_has_the_manually_calculated_propagation_cost_and_no_core()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", []));

        var run = Measure(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("propagation cost: 66.67% (6 reachable file pairs / 9)"), run.Output);
        Assert.True(run.OutputContains("core size: 0 files (0.00% of 3 authored files)"), run.Output);
    }

    [Fact]
    public void A_cycle_of_n_files_has_full_propagation_and_an_n_file_core()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", ["A"]));

        var run = Measure(repository);

        Assert.True(run.OutputContains("propagation cost: 100.00% (9 reachable file pairs / 9)"), run.Output);
        Assert.True(run.OutputContains("core size: 3 files (100.00% of 3 authored files)"), run.Output);
    }

    [Fact]
    public void A_complete_graph_has_full_propagation_and_a_full_core()
    {
        using var repository = Graph(
            ("A", ["B", "C"]),
            ("B", ["A", "C"]),
            ("C", ["A", "B"]));

        var run = Measure(repository);

        Assert.True(run.OutputContains("propagation cost: 100.00% (9 reachable file pairs / 9)"), run.Output);
        Assert.True(run.OutputContains("core size: 3 files (100.00% of 3 authored files)"), run.Output);
    }

    [Fact]
    public void A_core_with_a_periphery_weights_each_component_by_its_file_count()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", ["B"]),
            ("D", []));

        var run = Measure(repository);

        Assert.True(run.OutputContains("propagation cost: 50.00% (8 reachable file pairs / 16)"), run.Output);
        Assert.True(run.OutputContains("core size: 2 files (50.00% of 4 authored files)"), run.Output);
    }

    [Fact]
    public void Files_without_any_proven_edge_report_only_the_diagonal()
    {
        using var repository = Graph(
            ("A", []),
            ("B", []),
            ("C", []),
            ("D", []));

        var run = Measure(repository);

        Assert.True(run.OutputContains("propagation cost: 25.00% (4 reachable file pairs / 16)"), run.Output);
        Assert.True(run.OutputContains("core size: 0 files (0.00% of 4 authored files)"), run.Output);
    }

    [Fact]
    public void Metrics_are_informational_visible_and_stable_between_runs()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", []));

        var first = HarnessCli.Run(repository.Path, "check");
        var second = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, first.ExitCode);
        Assert.Equal(first.Output, second.Output);
        Assert.True(first.OutputContains("propagation cost:"), first.Output);
        Assert.True(first.OutputContains("core size:"), first.Output);
    }

    [Fact]
    public void Explain_names_both_formulas_evidence_limits_and_sources()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Propagation cost formula"), run.Output);
        Assert.True(run.OutputContains("Core size formula"), run.Output);
        Assert.True(run.OutputContains("Proven"), run.Output);
        Assert.True(run.OutputContains("10.1287/mnsc.1060.0552"), run.Output);
        Assert.True(run.OutputContains("S0048733314001012"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("Remediation"), run.Output);
    }

    private static CliRun Measure(RepositoryFixture repository)
        => HarnessCli.Run(repository.Path, "check", "--only", Check);

    private static RepositoryFixture Graph(params (string Name, string[] Dependencies)[] files)
    {
        var repository = Fixtures.Compliant(Frame.AllPresent());
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
}

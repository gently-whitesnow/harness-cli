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
    public void A_three_file_tree_has_the_manually_calculated_mean_reach_and_no_core()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", []));

        var run = Measure(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach: 2.00 files (6 reachable file pairs / 3 files; propagation cost 66.67%)"), run.Output);
        Assert.True(run.OutputContains("core size: 0 files (0.00% of 3 files)"), run.Output);
        Assert.True(run.OutputContains("scope: 3 authored files (no architecture zone; the repository is measured whole)"), run.Output);
    }

    [Fact]
    public void A_cycle_of_n_files_has_full_propagation_and_an_n_file_core()
    {
        using var repository = Graph(
            ("A", ["B"]),
            ("B", ["C"]),
            ("C", ["A"]));

        var run = Measure(repository);

        Assert.True(run.OutputContains("mean reach: 3.00 files (9 reachable file pairs / 3 files; propagation cost 100.00%)"), run.Output);
        Assert.True(run.OutputContains("core size: 3 files (100.00% of 3 files)"), run.Output);
    }

    [Fact]
    public void A_complete_graph_has_full_propagation_and_a_full_core()
    {
        using var repository = Graph(
            ("A", ["B", "C"]),
            ("B", ["A", "C"]),
            ("C", ["A", "B"]));

        var run = Measure(repository);

        Assert.True(run.OutputContains("mean reach: 3.00 files (9 reachable file pairs / 3 files; propagation cost 100.00%)"), run.Output);
        Assert.True(run.OutputContains("core size: 3 files (100.00% of 3 files)"), run.Output);
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

        Assert.True(run.OutputContains("mean reach: 2.00 files (8 reachable file pairs / 4 files; propagation cost 50.00%)"), run.Output);
        Assert.True(run.OutputContains("core size: 2 files (50.00% of 4 files)"), run.Output);
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

        Assert.True(run.OutputContains("mean reach: 1.00 files (4 reachable file pairs / 4 files; propagation cost 25.00%)"), run.Output);
        Assert.True(run.OutputContains("core size: 0 files (0.00% of 4 files)"), run.Output);
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
        Assert.True(first.OutputContains("mean reach:"), first.Output);
        Assert.True(first.OutputContains("core size:"), first.Output);
    }

    [Fact]
    public void Explain_names_both_formulas_evidence_limits_and_sources()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Mean reach formula"), run.Output);
        Assert.True(run.OutputContains("Scope"), run.Output);
        Assert.True(run.OutputContains("Core size formula"), run.Output);
        Assert.True(run.OutputContains("Proven"), run.Output);
        Assert.True(run.OutputContains("10.1287/mnsc.1060.0552"), run.Output);
        Assert.True(run.OutputContains("S0048733314001012"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("Remediation"), run.Output);
    }

    [Fact]
    public void Files_outside_the_architecture_zone_are_not_measured()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Architecture("""{ "standard": "sliced-dotnet/1" }"""))
            .WriteFile("src/App/Host/Program.cs", "namespace App.Host; sealed class Program;\n")
            .WriteFile(
                "src/App/Api/Features/Example/Endpoint.cs",
                Reference("App.Api.Features.Example", "Endpoint", "App.Application.Features.Example", ["UseCase"]))
            .WriteFile(
                "src/App/Application/Features/Example/UseCase.cs",
                "namespace App.Application.Features.Example;\n\npublic sealed class UseCase;\n")
            .WriteFile(
                "tests/App.Tests/EndpointTests.cs",
                Reference("App.Tests", "EndpointTests", "App.Api.Features.Example", ["Endpoint", "App.Application.Features.Example.UseCase"]))
            .Commit();

        var run = Measure(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach: 1.33 files (4 reachable file pairs / 3 files; propagation cost 44.44%)"), run.Output);
        Assert.True(run.OutputContains("scope: 3 files inside architecture zone [src/App]; 1 authored file outside the zones is not measured"), run.Output);
    }

    [Fact]
    public void A_library_without_a_zone_measures_every_authored_file()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent())
            .WriteFile("src/Lib/Core.cs", "namespace Lib;\n\npublic sealed class Core;\n")
            .WriteFile("tests/Lib.Tests/CoreTests.cs", Reference("Lib.Tests", "CoreTests", "Lib", ["Core"]))
            .Commit();

        var run = Measure(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach: 1.50 files (3 reachable file pairs / 2 files"), run.Output);
        Assert.True(run.OutputContains("scope: 2 authored files (no architecture zone"), run.Output);
    }

    [Fact]
    public void A_tracked_file_with_a_generated_marker_is_named_but_not_read()
    {
        using var repository = Graph(("A", ["B"]), ("B", []))
            .WriteFile(
                "src/Graph/Hub.cs",
                """
                // <auto-generated />
                namespace Graph;

                public sealed class Hub
                {
                    private A? a;
                    private B? b;
                }

                """)
            .Commit();

        var run = Measure(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("mean reach: 1.50 files (3 reachable file pairs / 2 files"), run.Output);
        Assert.True(
            run.OutputContains("generated markers: 1 tracked C# file with an <auto-generated> header is not read: src/Graph/Hub.cs"),
            run.Output);
    }

    private static string Reference(string module, string name, string imported, IReadOnlyList<string> used)
        => $$"""
        using {{imported}};

        namespace {{module}};

        public sealed class {{name}}
        {
        {{string.Join('\n', used.Select((type, index) => $"    private {type}? held{index};"))}}
        }

        """;

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

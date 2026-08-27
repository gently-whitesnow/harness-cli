namespace Harness.Tests;

public sealed class ArchitectureShapeTests
{
    private static readonly string[] Layers =
        ["Host", "Api", "Consumers", "Application", "Domain", "Infrastructure", "Shared"];

    private static readonly Dictionary<string, string[]> AllowedDependencies =
        new Dictionary<string, string[]>(StringComparer.Ordinal)
        {
            ["Host"] = Layers,
            ["Api"] = ["Application", "Shared"],
            ["Consumers"] = ["Application", "Shared"],
            ["Application"] = ["Domain", "Shared"],
            ["Domain"] = ["Shared"],
            ["Infrastructure"] = ["Application", "Domain", "Shared"],
            ["Shared"] = [],
        };

    [Theory]
    [InlineData("{}", "'architecture' must select a standard or declare applicability false")]
    [InlineData("{ \"standard\": \"sliced-dotnet/2\" }", "'architecture.standard' must be 'sliced-dotnet/1'")]
    [InlineData("{ \"standard\": \"sliced-dotnet/1\", \"layers\": [] }", "'architecture.layers' is not a key")]
    [InlineData("{ \"applicable\": false }", "'architecture.reason' must say why")]
    [InlineData("{ \"applicable\": false, \"reason\": \"library\", \"extra\": true }", "'architecture.extra' is not a key")]
    public void Invalid_architecture_section_ends_the_run_as_incomplete(string architecture, string explanation)
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Architecture(architecture));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(explanation, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_invariants_cannot_be_disabled_by_repository_policy()
    {
        using var repository = Fixtures.Compliant(
            Frame.AllPresent().Policy("architecture.sliced-dotnet", "off"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("cannot soften or disable blocking architecture invariants", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_required_and_input_layers_are_blocking()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("missing required layer 'Host'", run.Output, StringComparison.Ordinal);
        Assert.Contains("missing input layer: add Api/ or Consumers/", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_present_layer_with_only_a_placeholder_is_empty()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/.gitkeep", "")
            .WriteFile("src/Orders/Api/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("layer 'Host' is empty", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(".keep")]
    [InlineData(".gitignore")]
    public void Other_placeholder_markers_do_not_make_a_layer_nonempty(string marker)
    {
        using var repository = ArchitectureRepository()
            .WriteFile($"src/Orders/Host/{marker}", "")
            .WriteFile("src/Orders/Api/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("layer 'Host' is empty", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_close_layer_name_is_reported_as_a_typo()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Orders/Api/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .WriteFile("src/Orders/Infrastrcture/Adapter.cs", "sealed class Adapter;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("typo-in-layer-name", run.Output, StringComparison.Ordinal);
        Assert.Contains("'Infrastructure'", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Every_run_prints_all_detected_zones_layers_and_slices()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Orders/Api/Features/Sales/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .WriteFile("src/Billing/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Billing/Consumers/Worker.cs", "sealed class Worker;")
            .WriteFile("src/Billing/Application/Features/Finance/Invoices/Create.cs", "sealed class Create;")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "architecture.sliced-dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains(
            "architecture map: zone src/Orders · layers [Host, Api, Application] · slices [Sales]",
            run.Output,
            StringComparison.Ordinal);
        Assert.Contains(
            "architecture map: zone src/Billing · layers [Host, Consumers, Application] · slices [Finance/Invoices]",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Nested_feature_paths_are_not_lost_from_the_map()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Orders/Api/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Group/Sub/Deep/Handler.cs", "sealed class Handler;")
            .WriteFile("src/Orders/Application/Features/Grp2/FileInGroup.cs", "sealed class FileInGroup;")
            .WriteFile("src/Orders/Application/Features/Grp2/Slice/Handler.cs", "sealed class Handler;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("nested [Group/Sub/Deep, Grp2/Slice]", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Noncanonical_layer_directory_is_blocking()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Orders/Api/Endpoint.cs", "sealed class Endpoint;")
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", "sealed class Create;")
            .WriteFile("src/Orders/Tests/Fixture.cs", "sealed class Fixture;")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("noncanonical-layer-directory", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Not_applicable_architecture_still_prints_an_observation()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "architecture.sliced-dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("architecture map: not applicable — standalone fixture repository", run.Output, StringComparison.Ordinal);
    }

    public static TheoryData<string, string> ForbiddenLayerPairs()
    {
        var pairs = new TheoryData<string, string>();
        foreach (var from in Layers)
        {
            foreach (var to in Layers.Where(to => to != from && !AllowedDependencies[from].Contains(to)))
            {
                pairs.Add(from, to);
            }
        }

        return pairs;
    }

    [Theory]
    [MemberData(nameof(ForbiddenLayerPairs))]
    public void Every_forbidden_layer_pair_is_blocking(string from, string to)
    {
        using var repository = DependencyRepository(from, to, proven: true);

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"layer dependency {from} -> {to} is forbidden", run.Output, StringComparison.Ordinal);
        Assert.Contains($"src/Orders/{from}/From.cs", run.Output, StringComparison.Ordinal);
        Assert.Contains($"src/Orders/{to}/To.cs", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_inferred_forbidden_edge_does_not_block()
    {
        using var repository = DependencyRepository("Shared", "Host", proven: false);

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("layer dependency", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Infrastructure_persistence_may_read_any_domain_slice()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .WriteFile("src/Orders/Infrastructure/Persistence/Db.cs", ProvenReference("Fixture.Infrastructure", "Db", "Fixture.Domain", "Entity"))
            .WriteFile("src/Orders/Domain/Inventory/Entity.cs", EmptyType("Fixture.Domain", "Entity"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("layer dependency", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_proven_edge_between_zones_is_blocking()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Orders.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", ProvenReference("Orders.Api", "Endpoint", "Billing.Application", "UseCase"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Orders.Application", "Create"))
            .WriteFile("src/Billing/Host/Program.cs", EmptyType("Billing.Host", "Program"))
            .WriteFile("src/Billing/Api/Endpoint.cs", EmptyType("Billing.Api", "Endpoint"))
            .WriteFile("src/Billing/Application/Features/Invoices/UseCase.cs", EmptyType("Billing.Application", "UseCase"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("cross-zone dependency is forbidden", run.Output, StringComparison.Ordinal);
        Assert.Contains("src/Orders/Api/Endpoint.cs", run.Output, StringComparison.Ordinal);
        Assert.Contains("src/Billing/Application/Features/Invoices/UseCase.cs", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_csharp_file_at_the_zone_root_is_blocking()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Loose.cs", EmptyType("Fixture", "Loose"))
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("src/Orders/Loose.cs", run.Output, StringComparison.Ordinal);
        Assert.Contains("outside every canonical layer", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_describes_the_fitness_function_dag_and_lexical_limit()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", "architecture.sliced-dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("fitness function", run.Output, StringComparison.Ordinal);
        Assert.Contains("Infrastructure -> Application, Domain, Shared", run.Output, StringComparison.Ordinal);
        Assert.Contains("Inferred", run.Output, StringComparison.Ordinal);
        Assert.Contains("member access", run.Output, StringComparison.Ordinal);
    }

    private static RepositoryFixture ArchitectureRepository()
        => Fixtures.Compliant(Frame.AllPresent().Architecture("""{ "standard": "sliced-dotnet/1" }"""));

    private static RepositoryFixture DependencyRepository(string from, string to, bool proven)
    {
        var reference = proven
            ? ProvenReference("Fixture.From", "From", "Fixture.To", "To")
            : InferredReference("Fixture.From", "From", "Fixture.To", "To");

        return ArchitectureRepository()
            .WriteFile("src/Orders/Host/Baseline.cs", EmptyType("Fixture.Baseline", "HostBaseline"))
            .WriteFile("src/Orders/Api/Baseline.cs", EmptyType("Fixture.Baseline", "ApiBaseline"))
            .WriteFile("src/Orders/Application/Features/Sales/Baseline.cs", EmptyType("Fixture.Baseline", "ApplicationBaseline"))
            .WriteFile($"src/Orders/{from}/From.cs", reference)
            .WriteFile($"src/Orders/{to}/To.cs", EmptyType("Fixture.To", "To"))
            .Commit();
    }

    private static string ProvenReference(string module, string name, string imported, string used)
        => $$"""
        using {{imported}};

        namespace {{module}};

        public sealed class {{name}}
        {
            private {{used}}? held;
        }

        """;

    private static string InferredReference(string module, string name, string imported, string used)
        => $$"""
        using {{imported}};

        namespace {{module}};

        public sealed class {{name}}
        {
            public static string Describe() => {{used}}.Label;
        }

        """;

    private static string EmptyType(string module, string name)
        => $$"""
        namespace {{module}};

        public sealed class {{name}}
        {
            public const string Label = "x";
        }

        """;

    private static CliRun Shape(RepositoryFixture repository)
        => HarnessCli.RunVerbose(repository.Path, "check", "--only", "architecture.sliced-dotnet");
}

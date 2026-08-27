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
    public void Generated_csharp_at_the_zone_root_is_not_part_of_the_architecture_graph()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Generated.g.cs", EmptyType("Fixture", "Generated"))
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("outside every canonical layer", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Noncanonical_directory_is_not_repeated_for_every_csharp_file()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .WriteFile("src/Orders/Helpers/One.cs", EmptyType("Fixture.Helpers", "One"))
            .WriteFile("src/Orders/Helpers/Two.cs", EmptyType("Fixture.Helpers", "Two"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("noncanonical-layer-directory: 'Helpers'", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("outside every canonical layer", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_edges_in_one_layer_pair_are_folded_in_the_summary()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile(
                "src/Orders/Api/Endpoint.cs",
                ProvenReferences("Fixture.Api", "Endpoint", "Fixture.Infrastructure", ["One", "Two", "Three", "Four", "Five"]))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .WriteFile("src/Orders/Infrastructure/One.cs", EmptyType("Fixture.Infrastructure", "One"))
            .WriteFile("src/Orders/Infrastructure/Two.cs", EmptyType("Fixture.Infrastructure", "Two"))
            .WriteFile("src/Orders/Infrastructure/Three.cs", EmptyType("Fixture.Infrastructure", "Three"))
            .WriteFile("src/Orders/Infrastructure/Four.cs", EmptyType("Fixture.Infrastructure", "Four"))
            .WriteFile("src/Orders/Infrastructure/Five.cs", EmptyType("Fixture.Infrastructure", "Five"))
            .Commit();

        var run = Shape(repository);
        var all = HarnessCli.Run(repository.Path, "check", "--only", "architecture.sliced-dotnet", "--all");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(1, Occurrences(run.Output, "layer dependency Api -> Infrastructure"));
        Assert.Contains("and 4 more file pairs", run.Output, StringComparison.Ordinal);
        Assert.Equal(5, Occurrences(all.Output, "layer dependency Api -> Infrastructure"));
    }

    [Fact]
    public void Cross_slice_dependency_through_ordinary_contracts_is_blocking()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Create.cs",
                ProvenReference("Fixture.Sales", "Create", "Fixture.Inventory", "InventoryContract"))
            .WriteFile(
                "src/Orders/Application/Features/Inventory/Contracts/InventoryContract.cs",
                EmptyType("Fixture.Inventory", "InventoryContract"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("cross-slice dependency Application/Sales -> Application/Inventory", run.Output, StringComparison.Ordinal);
        Assert.Contains("merge the slices, move the shared concept down", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("Domain/Money.cs")]
    [InlineData("Application/Features/Money.cs")]
    public void Csharp_file_directly_in_a_slice_root_is_blocking(string relativePath)
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Sales", "Create"))
            .WriteFile($"src/Orders/{relativePath}", EmptyType("Fixture.Loose", "Money"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"src/Orders/{relativePath}", run.Output, StringComparison.Ordinal);
        Assert.Contains("outside every slice", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("failed to run", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Cross_api_rejects_an_import_from_a_third_slice()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Baseline.cs", EmptyType("Fixture.Sales", "SalesBaseline"))
            .WriteFile(
                "src/Orders/Application/Features/Billing/Create.cs",
                ProvenReference("Fixture.Billing", "Create", "Fixture.Inventory", "ForSales"))
            .WriteFile(
                "src/Orders/Application/Features/Inventory/Contracts/X/Sales/ForSales.cs",
                EmptyType("Fixture.Inventory", "ForSales"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("may be imported only by slice 'Sales'", run.Output, StringComparison.Ordinal);
        Assert.Contains("slice 'Billing'", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Mirror_cannot_sidestep_an_application_slice_contracts()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile(
                "src/Orders/Api/Features/Sales/Endpoint.cs",
                ProvenReference("Fixture.Api", "Endpoint", "Fixture.Sales", "Handler"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Handler.cs",
                EmptyType("Fixture.Sales", "Handler"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("Application public API sidestep into slice 'Sales'", run.Output, StringComparison.Ordinal);
        Assert.Contains("through Contracts/", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Mirror_may_consume_another_application_slice_public_contract()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile(
                "src/Orders/Api/Features/Sales/Endpoint.cs",
                ProvenReference("Fixture.Api", "Endpoint", "Fixture.Inventory", "InventoryContract"))
            .WriteFile("src/Orders/Application/Features/Sales/Baseline.cs", EmptyType("Fixture.Sales", "Baseline"))
            .WriteFile(
                "src/Orders/Application/Features/Inventory/Contracts/InventoryContract.cs",
                EmptyType("Fixture.Inventory", "InventoryContract"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("cross-slice dependency", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("public API sidestep", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Application_slice_may_read_a_different_domain_slice()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Inventory/Baseline.cs", EmptyType("Fixture.Inventory", "Baseline"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Create.cs",
                ProvenReference("Fixture.Sales", "Create", "Fixture.Domain", "InventoryItem"))
            .WriteFile(
                "src/Orders/Domain/Inventory/InventoryItem.cs",
                EmptyType("Fixture.Domain", "InventoryItem"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("cross-slice dependency", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_consumer_may_import_an_application_cross_api()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Create.cs",
                ProvenReference("Fixture.Sales", "Create", "Fixture.Inventory", "ForSales"))
            .WriteFile(
                "src/Orders/Application/Features/Inventory/Contracts/X/Sales/ForSales.cs",
                EmptyType("Fixture.Inventory", "ForSales"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("cross-slice dependency", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("may be imported only", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Named_consumer_may_import_a_domain_cross_api()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Inventory/Baseline.cs", EmptyType("Fixture.Inventory", "Baseline"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Create.cs",
                ProvenReference("Fixture.Sales", "Create", "Fixture.Domain", "ForSales"))
            .WriteFile(
                "src/Orders/Domain/Inventory/X/Sales/ForSales.cs",
                EmptyType("Fixture.Domain", "ForSales"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("may be imported only", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Repeated_cross_slice_edges_are_folded_by_slice_pair()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Create.cs",
                ProvenReferences("Fixture.Sales", "Create", "Fixture.Inventory", ["One", "Two"]))
            .WriteFile("src/Orders/Application/Features/Inventory/One.cs", EmptyType("Fixture.Inventory", "One"))
            .WriteFile("src/Orders/Application/Features/Inventory/Two.cs", EmptyType("Fixture.Inventory", "Two"))
            .Commit();

        var run = Shape(repository);
        var all = HarnessCli.Run(repository.Path, "check", "--only", "architecture.sliced-dotnet", "--all");

        Assert.Equal(1, run.ExitCode);
        Assert.Equal(1, Occurrences(run.Output, "cross-slice dependency Application/Sales -> Application/Inventory"));
        Assert.Contains("and 1 more file pairs", run.Output, StringComparison.Ordinal);
        Assert.Equal(2, Occurrences(all.Output, "cross-slice dependency Application/Sales -> Application/Inventory"));
    }

    [Fact]
    public void File_in_a_group_directory_intentionally_turns_the_group_into_one_slice()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Trade/Marker.cs", EmptyType("Fixture.Trade", "Marker"))
            .WriteFile(
                "src/Orders/Application/Features/Trade/Sales/Create.cs",
                ProvenReference("Fixture.Sales", "Create", "Fixture.Inventory", "Item"))
            .WriteFile(
                "src/Orders/Application/Features/Trade/Inventory/Item.cs",
                EmptyType("Fixture.Inventory", "Item"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("slices [Trade]", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("cross-slice dependency", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Host_may_access_application_slice_implementation()
    {
        using var repository = ArchitectureRepository()
            .WriteFile(
                "src/Orders/Host/Program.cs",
                ProvenReference("Fixture.Host", "Program", "Fixture.Sales", "Handler"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile(
                "src/Orders/Application/Features/Sales/Handler.cs",
                EmptyType("Fixture.Sales", "Handler"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("public API sidestep", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Compact_report_limits_distinct_dependency_groups()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Target.cs", EmptyType("Fixture.Targets", "HostTarget"))
            .WriteFile("src/Orders/Api/Target.cs", EmptyType("Fixture.Targets", "ApiTarget"))
            .WriteFile("src/Orders/Consumers/Target.cs", EmptyType("Fixture.Targets", "ConsumersTarget"))
            .WriteFile("src/Orders/Application/Features/Sales/Target.cs", EmptyType("Fixture.Targets", "ApplicationTarget"))
            .WriteFile("src/Orders/Domain/Inventory/Target.cs", EmptyType("Fixture.Targets", "DomainTarget"))
            .WriteFile("src/Orders/Infrastructure/Target.cs", EmptyType("Fixture.Targets", "InfrastructureTarget"))
            .WriteFile(
                "src/Orders/Shared/From.cs",
                ProvenReferences(
                    "Fixture.Shared",
                    "From",
                    "Fixture.Targets",
                    ["HostTarget", "ApiTarget", "ConsumersTarget", "ApplicationTarget", "DomainTarget", "InfrastructureTarget"]))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("6 architecture dependency groups were proved", run.Output, StringComparison.Ordinal);
        Assert.Contains("the first 5 are listed above", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Shape_findings_survive_an_unreadable_csharp_graph()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Application/Features/Sales/Create.cs", EmptyType("Fixture.Application", "Create"))
            .WriteFile("src/Orders/Helpers/Broken.cs", EmptyType("Fixture.Helpers", "Broken"))
            .Commit()
            .PointIndexAtMissingObject("src/Orders/Helpers/Broken.cs")
            .Remove("src/Orders/Helpers/Broken.cs");

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("missing required layer 'Host'", run.Output, StringComparison.Ordinal);
        Assert.Contains("noncanonical-layer-directory: 'Helpers'", run.Output, StringComparison.Ordinal);
        Assert.Contains("architecture map: zone src/Orders", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Architecture_map_survives_an_unreadable_csharp_graph()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", EmptyType("Fixture.Host", "Program"))
            .WriteFile("src/Orders/Api/Endpoint.cs", EmptyType("Fixture.Api", "Endpoint"))
            .WriteFile("src/Orders/Application/Features/Sales/Broken.cs", EmptyType("Fixture.Application", "Broken"))
            .Commit()
            .PointIndexAtMissingObject("src/Orders/Application/Features/Sales/Broken.cs")
            .Remove("src/Orders/Application/Features/Sales/Broken.cs");

        var run = Shape(repository);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("architecture map: zone src/Orders", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Explain_describes_the_fitness_function_dag_and_lexical_limit()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", "architecture.sliced-dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("fitness function", run.Output, StringComparison.Ordinal);
        Assert.Contains("Infrastructure -> Application/Contracts, Domain, Shared", run.Output, StringComparison.Ordinal);
        Assert.Contains("layer-pair stage accepts Infrastructure -> Application", run.Output, StringComparison.Ordinal);
        Assert.Contains("Domain is the common vocabulary", run.Output, StringComparison.Ordinal);
        Assert.Contains("Slice isolation is evaluated within one layer", run.Output, StringComparison.Ordinal);
        Assert.Contains("makes <Name> a slice", run.Output, StringComparison.Ordinal);
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

    private static string ProvenReferences(
        string module,
        string name,
        string imported,
        IReadOnlyList<string> used)
        => $$"""
        using {{imported}};

        namespace {{module}};

        public sealed class {{name}}
        {
        {{string.Join('\n', used.Select((type, index) => $"    private {type}? held{index};"))}}
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

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;
}

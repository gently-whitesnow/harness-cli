namespace Harness.Tests;

/// <summary>
/// ADR-0041: the layer is the assembly. Every canonical layer holding C# sources is exactly
/// one project, a project compiles only its own layer, and project references between the
/// zone's projects follow the layer table.
/// </summary>
public sealed class ArchitectureLayerProjectTests
{
    [Fact]
    public void A_zone_with_one_project_per_layer_passes()
    {
        using var repository = SixProjectZone().Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("layer-project", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("linked-compilation", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Without_any_tracked_project_the_layer_assembly_invariants_stay_silent()
    {
        using var repository = ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", Type("Orders.HostPart", "Program"))
            .WriteFile("src/Orders/Api/Sales/Endpoint.cs", Type("Orders.ApiPart", "Endpoint"))
            .WriteFile("src/Orders/Application/Sales/Create.cs", Type("Orders.ApplicationPart", "Create"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("layer-project-missing", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_layer_with_sources_but_no_project_is_blocking()
    {
        using var repository = SixProjectZone()
            .Remove("src/Orders/Application/Orders.Application.csproj")
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "layer-project-missing: layer 'Application' contains C# sources but no tracked .csproj",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_second_project_in_one_layer_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile("src/Orders/Api/Orders.Api.Extra.csproj", Project())
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("layer-project-count: layer 'Api' contains 2 projects", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_outside_every_canonical_layer_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile("src/Orders/Orders.csproj", Project())
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "project-outside-layer: project 'src/Orders/Orders.csproj'",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void Linked_compilation_of_another_layer_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile(
                "src/Orders/Host/Orders.Host.csproj",
                Project(
                    ["../Application/Orders.Application.csproj"],
                    compile: "../Api/**/*.cs"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "linked-compilation: Compile Include '../Api/**/*.cs' reaches outside layer 'Host'",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_compile_item_inside_the_own_layer_is_accepted()
    {
        using var repository = SixProjectZone()
            .WriteFile(
                "src/Orders/Host/Orders.Host.csproj",
                Project(
                    [
                        "../Api/Orders.Api.csproj",
                        "../Consumers/Orders.Consumers.csproj",
                        "../Application/Orders.Application.csproj",
                        "../Domain/Orders.Domain.csproj",
                        "../Infrastructure/Orders.Infrastructure.csproj",
                    ],
                    compile: "Generated/Extra.cs"))
            .WriteFile("src/Orders/Host/Generated/Extra.cs", Type("Orders.HostPart", "Extra"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("linked-compilation", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_reference_outside_the_layer_table_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile(
                "src/Orders/Api/Orders.Api.csproj",
                Project(
                    "../Application/Orders.Application.csproj",
                    "../Infrastructure/Orders.Infrastructure.csproj"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "layer project reference Api -> Infrastructure is forbidden by sliced-dotnet/1",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void An_upward_project_reference_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile(
                "src/Orders/Application/Orders.Application.csproj",
                Project("../Domain/Orders.Domain.csproj", "../Api/Orders.Api.csproj"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(
            "layer project reference Application -> Api is forbidden by sliced-dotnet/1",
            run.Output,
            StringComparison.Ordinal);
    }

    [Fact]
    public void A_project_reference_into_another_zone_is_blocking()
    {
        using var repository = SixProjectZone()
            .WriteFile("src/Billing/Host/Program.cs", Type("Billing.HostPart", "Program"))
            .WriteFile("src/Billing/Host/Billing.Host.csproj", Project("../Application/Billing.Application.csproj"))
            .WriteFile("src/Billing/Api/Invoices/Endpoint.cs", Type("Billing.ApiPart", "Endpoint"))
            .WriteFile("src/Billing/Api/Billing.Api.csproj", Project("../Application/Billing.Application.csproj"))
            .WriteFile("src/Billing/Application/Invoices/Create.cs", Type("Billing.ApplicationPart", "Create"))
            .WriteFile(
                "src/Billing/Application/Billing.Application.csproj",
                Project("../../Orders/Domain/Orders.Domain.csproj"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("cross-zone project reference is forbidden", run.Output, StringComparison.Ordinal);
        Assert.Contains("'src/Orders/Domain/Orders.Domain.csproj' in zone 'src/Orders'", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void An_item_path_carrying_an_msbuild_property_is_not_judged()
    {
        using var repository = SixProjectZone()
            .WriteFile(
                "src/Orders/Host/Orders.Host.csproj",
                Project(
                    ["../Application/Orders.Application.csproj"],
                    compile: "$(IntermediateOutputPath)Extra.g.cs"))
            .Commit();

        var run = Shape(repository);

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("linked-compilation", run.Output, StringComparison.Ordinal);
    }

    private static RepositoryFixture ArchitectureRepository()
        => Fixtures.Compliant(Frame.AllPresent()
            .Architecture("""{ "standard": "sliced-dotnet/1" }""")
            .Policy("architecture.sliced-dotnet", "required"));

    private static RepositoryFixture SixProjectZone()
        => ArchitectureRepository()
            .WriteFile("src/Orders/Host/Program.cs", Type("Orders.HostPart", "Program"))
            .WriteFile(
                "src/Orders/Host/Orders.Host.csproj",
                Project(
                    "../Api/Orders.Api.csproj",
                    "../Consumers/Orders.Consumers.csproj",
                    "../Application/Orders.Application.csproj",
                    "../Domain/Orders.Domain.csproj",
                    "../Infrastructure/Orders.Infrastructure.csproj"))
            .WriteFile("src/Orders/Api/Sales/Endpoint.cs", Type("Orders.ApiPart", "Endpoint"))
            .WriteFile(
                "src/Orders/Api/Orders.Api.csproj",
                Project("../Application/Orders.Application.csproj", "../Domain/Orders.Domain.csproj"))
            .WriteFile("src/Orders/Consumers/Sales/Subscription.cs", Type("Orders.ConsumersPart", "Subscription"))
            .WriteFile(
                "src/Orders/Consumers/Orders.Consumers.csproj",
                Project("../Application/Orders.Application.csproj", "../Domain/Orders.Domain.csproj"))
            .WriteFile("src/Orders/Application/Sales/Create.cs", Type("Orders.ApplicationPart", "Create"))
            .WriteFile(
                "src/Orders/Application/Orders.Application.csproj",
                Project("../Domain/Orders.Domain.csproj"))
            .WriteFile("src/Orders/Domain/Sales/Sale.cs", Type("Orders.DomainPart", "Sale"))
            .WriteFile("src/Orders/Domain/Orders.Domain.csproj", Project())
            .WriteFile("src/Orders/Infrastructure/Sales/SaleRecord.cs", Type("Orders.InfrastructurePart", "SaleRecord"))
            .WriteFile(
                "src/Orders/Infrastructure/Orders.Infrastructure.csproj",
                Project("../Application/Orders.Application.csproj", "../Domain/Orders.Domain.csproj"));

    private static string Project(params string[] references) => Project(references, compile: null);

    private static string Project(IReadOnlyList<string> references, string? compile)
    {
        var items = references
            .Select(reference => $"    <ProjectReference Include=\"{reference}\" />")
            .ToList();
        if (compile is not null)
        {
            items.Add($"    <Compile Include=\"{compile}\" />");
        }

        return $$"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
        {{string.Join('\n', items)}}
          </ItemGroup>
        </Project>

        """;
    }

    private static string Type(string module, string name)
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

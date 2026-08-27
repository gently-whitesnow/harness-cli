namespace Harness.Tests;

public sealed class ArchitectureShapeTests
{
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

    private static RepositoryFixture ArchitectureRepository()
        => Fixtures.Compliant(Frame.AllPresent().Architecture("""{ "standard": "sliced-dotnet/1" }"""));

    private static CliRun Shape(RepositoryFixture repository)
        => HarnessCli.RunVerbose(repository.Path, "check", "--only", "architecture.sliced-dotnet");
}

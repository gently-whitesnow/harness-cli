namespace Harness.Tests;

public sealed class BuildPropertiesTests
{
    [Fact]
    public void Project_without_directory_build_props_fails()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/App.csproj"), run.Output);
        Assert.True(run.OutputContains("not covered by a tracked Directory.Build.props"), run.Output);
    }

    [Fact]
    public void Nearest_scoped_hardened_props_covers_project()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("apps/api/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Missing_hardening_property_and_weakening_override_fail()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "    <Deterministic>true</Deterministic>\n",
                string.Empty,
                StringComparison.Ordinal))
            .WriteFile(
                "src/App/App.csproj",
                Fixtures.SimpleSdkProject.Replace(
                    "    <TargetFramework>net10.0</TargetFramework>",
                    "    <TargetFramework>net10.0</TargetFramework>\n    <Nullable>disable</Nullable>",
                    StringComparison.Ordinal))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("must set Deterministic to true"), run.Output);
        Assert.True(run.OutputContains("overrides central Nullable"), run.Output);
    }

    [Fact]
    public void Conflicting_value_inside_central_props_fails()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "    <Nullable>enable</Nullable>",
                "    <Nullable>enable</Nullable>\n    <Nullable Condition=\"'$(Legacy)' == 'true'\">disable</Nullable>",
                StringComparison.Ordinal))
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("sets Nullable to conflicting value 'disable'"), run.Output);
    }

    [Fact]
    public void Identical_target_framework_in_every_project_must_be_centralized()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("repeats shared TargetFramework 'net10.0'"), run.Output);
    }
}

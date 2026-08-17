namespace Harness.Tests;

public sealed class SolutionFormatTests
{
    [Fact]
    public void Legacy_solution_fails()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("App.sln", "legacy")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "solution-format.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("legacy .sln is tracked"), run.Output);
    }

    [Fact]
    public void Multiple_projects_without_slnx_fail()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "solution-format.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("no tracked .slnx solution"), run.Output);
    }

    [Fact]
    public void Slnx_must_cover_every_project()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("App.slnx", "<Solution><Project Path=\"src/One/One.csproj\" /></Solution>")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "solution-format.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/Two/Two.csproj"), run.Output);
        Assert.True(run.OutputContains("not included in any tracked .slnx"), run.Output);
    }

    [Fact]
    public void Scoped_slnx_paths_cover_projects()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("apps/api/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .WriteFile(
                "apps/api/Api.slnx",
                "<Solution><Project Path=\"One/One.csproj\" /><Project Path=\"Two/Two.csproj\" /></Solution>")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "solution-format.dotnet");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Dotnet_applicability_disables_all_three_checks()
    {
        using var repository = Fixtures.Compliant(
                Frame.AllPresent().NotApplicableTo("dotnet", "repository keeps example projects only"))
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.Equal(3, run.Output.Split("outcome: not applicable", StringSplitOptions.None).Length - 1);
        Assert.True(run.OutputContains("repository keeps example projects only"), run.Output);
    }
}

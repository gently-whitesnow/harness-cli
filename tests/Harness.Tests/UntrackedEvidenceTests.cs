namespace Harness.Tests;

/// <summary>
/// A file that exists on disk but not in the Git index is invisible to the harness, and the
/// verdict says so. These tests pin the diagnostic that tells the two invisible cases apart:
/// the file was never written, or it was written and never staged.
/// </summary>
public sealed class UntrackedEvidenceTests
{
    [Fact]
    public void Unstaged_central_packages_file_is_named_as_missing_from_the_index()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/App/App.csproj", Project)
            .Commit()
            .WriteFile("apps/api/Directory.Packages.props", Packages);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("no tracked Directory.Packages.props"), run.Output);
        Assert.True(run.OutputContains("not in the index  apps/api/Directory.Packages.props"), run.Output);
        Assert.True(run.OutputContains("git add"), run.Output);
    }

    [Fact]
    public void The_same_file_passes_once_it_is_staged()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/App/App.csproj", Project)
            .Commit()
            .WriteFile("apps/api/Directory.Packages.props", Packages);

        repository.Git("add", "apps/api/Directory.Packages.props");
        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("not in the index"), run.Output);
    }

    [Fact]
    public void A_file_that_was_never_written_gets_the_finding_without_the_hint()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/App/App.csproj", Project)
            .Commit()
            .WriteFile("apps/api/notes.txt", "unrelated");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("no tracked Directory.Packages.props"), run.Output);
        Assert.False(run.OutputContains("not in the index"), run.Output);
    }

    [Fact]
    public void The_hint_survives_without_verbose()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("apps/api/App/App.csproj", Project)
            .Commit()
            .WriteFile("apps/api/Directory.Packages.props", Packages);

        var run = HarnessCli.Run(repository.Path, "check", "--only", "central-packages.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("not in the index  apps/api/Directory.Packages.props"), run.Output);
    }

    [Fact]
    public void An_unstaged_solution_is_matched_by_its_extension()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject)
            .Commit()
            .WriteFile("App.slnx", "<Solution />");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "solution-format.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("no tracked .slnx solution"), run.Output);
        Assert.True(run.OutputContains("not in the index  App.slnx"), run.Output);
    }

    [Fact]
    public void An_unstaged_root_instruction_document_is_named()
    {
        using var repository = Fixtures.Framed()
            .Commit()
            .WriteFile("AGENTS.md", "# Root\n\nNavigation.\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("not in the index  AGENTS.md"), run.Output);
    }

    private const string Project =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <ItemGroup>
            <PackageReference Include="Example" />
          </ItemGroup>
        </Project>
        """;

    private const string Packages =
        """
        <Project>
          <PropertyGroup>
            <ManagePackageVersionsCentrally>true</ManagePackageVersionsCentrally>
          </PropertyGroup>
          <ItemGroup>
            <PackageVersion Include="Example" Version="1.0.0" />
          </ItemGroup>
        </Project>
        """;
}

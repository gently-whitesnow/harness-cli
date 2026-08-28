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

    /// <summary>
    /// The first thing every repository does: `harness init` writes the frame and deliberately
    /// leaves it unstaged. The frame carries no finding — the run is incomplete — so only a
    /// declaration made by the check itself can explain it.
    /// </summary>
    [Fact]
    public void An_unstaged_frame_written_by_init_is_named()
    {
        using var repository = Fixtures.WithoutAFrame();

        HarnessCli.RunWithInput(repository.Path, "application\n", "init");
        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.config");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("not in the index  .harness.json"), run.Output);
    }

    /// <summary>
    /// An outcome of "not applicable" hides the same trap: a project nobody staged looks
    /// exactly like a repository that has no .NET projects at all.
    /// </summary>
    [Fact]
    public void An_unstaged_project_is_named_although_nothing_was_applicable()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("no tracked SDK-style .NET projects"), run.Output);
        Assert.True(run.OutputContains("not in the index  src/App/App.csproj"), run.Output);
    }

    /// <summary>
    /// The declaration is required of every check and printed, so a check that reports a file
    /// as missing cannot answer this question silently or leave the answer invisible.
    /// </summary>
    [Fact]
    public void Every_shipped_check_states_its_named_evidence()
    {
        using var repository = Fixtures.Compliant();

        foreach (var id in HarnessCli.ShippedCheckIds(repository.Path))
        {
            var explain = HarnessCli.Run(repository.Path, "explain", id);
            Assert.True(explain.OutputContains("Named evidence"), explain.Output);
        }
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

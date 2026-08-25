namespace Harness.Tests;

/// <summary>
/// The pinned release is the contract: any binary that still knows it reaches the same
/// verdict, so local and CI installations do not have to match.
/// </summary>
[Trait("Category", "Publication")]
public sealed class ReleaseContractTests
{
    [Fact]
    public void Version_reports_the_release_the_build_stamped()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "version");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith($"harness {Release.Current}", run.StandardOutput, StringComparison.Ordinal);
    }

    [Fact]
    public void A_report_names_the_binary_and_the_pin_it_ran()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains($"harness {Release.Current}", run.Output, StringComparison.Ordinal);
        Assert.Contains($"repository pins {Release.Current}", run.Output, StringComparison.Ordinal);
    }

    /// <summary>
    /// The invariant the distribution rests on: a newer binary changes the version it reports
    /// and nothing else. Only `harness upgrade`, a tracked edit, changes a verdict.
    /// </summary>
    [Fact]
    public void A_newer_binary_reaches_the_same_verdict_on_the_same_pin()
    {
        var newer = BuildStampedAs("1.999.0");
        using var repository = Fixtures.Compliant();

        var pinned = HarnessCli.Run(repository.Path, "check");
        var updated = ProcessLauncher.Run(newer, ["check"], repository.Path);

        Assert.Equal(pinned.ExitCode, updated.ExitCode);
        Assert.Contains($"harness 1.999.0 · repository pins {Release.Current}", updated.Output, StringComparison.Ordinal);
        Assert.Equal(WithoutTheVersionLine(pinned.Output), WithoutTheVersionLine(updated.Output));
    }

    /// <summary>The preview exists so the cost of a newer release is visible before a commit.</summary>
    [Fact]
    public void Upgrade_previews_the_new_pin_before_it_writes_one()
    {
        var newer = BuildStampedAs("1.2.0");
        using var repository = Fixtures.Compliant();
        var frame = repository.Absolute(".harness.json");

        var preview = ProcessLauncher.Run(newer, ["upgrade", "--dry-run"], repository.Path);

        Assert.Equal(0, preview.ExitCode);
        Assert.Contains(
            $"Would raise .harness.json from {Release.Current} to 1.2.0",
            preview.Output,
            StringComparison.Ordinal);
        Assert.Contains("Nothing was written.", preview.Output, StringComparison.Ordinal);
        Assert.Contains($"\"version\": \"{Release.Current}\"", File.ReadAllText(frame), StringComparison.Ordinal);

        var raised = ProcessLauncher.Run(newer, ["upgrade"], repository.Path);

        Assert.Equal(0, raised.ExitCode);
        Assert.Contains(
            $"Raised .harness.json from {Release.Current} to 1.2.0",
            raised.Output,
            StringComparison.Ordinal);
        Assert.Contains("\"version\": \"1.2.0\"", File.ReadAllText(frame), StringComparison.Ordinal);
    }

    /// <summary>A binary that does not ship the pinned release refuses rather than guessing.</summary>
    [Fact]
    public void A_binary_older_than_the_pin_refuses_to_verify()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("99.0.0"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("newer than this binary"), run.Output);
        Assert.True(run.OutputContains("update the harness"), run.Output);
    }

    private static string WithoutTheVersionLine(string output)
        => string.Join(
            '\n',
            output.Split('\n').Where(line => !line.TrimStart().StartsWith("harness 1.", StringComparison.Ordinal)));

    /// <summary>Builds the CLI as a different release by overriding the one MSBuild property.</summary>
    private static string BuildStampedAs(string version)
    {
        var directory = Path.Combine(Path.GetTempPath(), "harness-release-" + Guid.NewGuid().ToString("n"));
        var build = ProcessLauncher.Run(
            "dotnet",
            [
                "build", Path.Combine("src", "Harness", "Harness.csproj"),
                "--configuration", "Debug",
                $"-p:HarnessVersion={version}",
                $"-p:BaseIntermediateOutputPath={Path.Combine(directory, "obj")}{Path.DirectorySeparatorChar}",
                "--output", Path.Combine(directory, "bin"),
            ],
            Release.RepositoryRoot());

        Assert.True(build.ExitCode == 0, $"Building the harness as {version} failed:\n{build.Output}");

        var executable = Path.Combine(directory, "bin", "harness");
        Assert.True(File.Exists(executable), $"Built executable not found:\n{build.Output}");
        return executable;
    }
}

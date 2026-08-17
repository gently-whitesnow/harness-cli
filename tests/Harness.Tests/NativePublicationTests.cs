using System.Runtime.InteropServices;

namespace Harness.Tests;

/// <summary>
/// Standalone distribution is an exercised behavior, not a packaging claim: the
/// executable is published for this platform with NativeAOT and then smoke-tested as a
/// separate process against a passing and a failing repository.
/// </summary>
[Trait("Category", "Publication")]
public sealed class NativePublicationTests
{
    [Fact]
    public void Published_native_executable_is_self_contained_and_checks_repositories()
    {
        var (executable, buildOutput) = Publish();

        Assert.False(
            buildOutput.Contains("warning IL", StringComparison.Ordinal),
            "NativeAOT publication produced trim or AOT warnings:\n" + buildOutput);

        using var compliant = Fixtures.Compliant();
        var passing = RunPublished(executable, compliant.Path);
        Assert.Equal(0, passing.ExitCode);
        Assert.True(passing.Output.StartsWith("PASS", StringComparison.Ordinal), passing.Output);

        using var violating = Fixtures.Compliant().WriteLines("AGENTS.md", 400).Commit();
        var failing = RunPublished(executable, violating.Path);
        Assert.Equal(1, failing.ExitCode);
    }

    private static CliRun RunPublished(string executable, string repositoryPath)
        => ProcessLauncher.Run(
            executable,
            ["check"],
            repositoryPath,
            // A published binary must not depend on the SDK that produced it.
            removeFromEnvironment: ["DOTNET_ROOT", "MSBuildExtensionsPath"]);

    private static (string Executable, string BuildOutput) Publish()
    {
        var outputDirectory = Path.Combine(
            Path.GetTempPath(), "harness-publish-" + Guid.NewGuid().ToString("n"));

        var publication = ProcessLauncher.Run(
            "dotnet",
            [
                "publish", Path.Combine("src", "Harness", "Harness.csproj"),
                "--configuration", "Release",
                "--runtime", RuntimeInformation.RuntimeIdentifier,
                "--output", outputDirectory,
            ],
            RepositoryRoot());

        Assert.True(publication.ExitCode == 0, "NativeAOT publication failed:\n" + publication.Output);

        var executable = Path.Combine(outputDirectory, "harness");
        Assert.True(File.Exists(executable), "Published executable not found:\n" + publication.Output);
        return (executable, publication.Output);
    }

    private static string RepositoryRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "Harness.slnx")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not locate the solution directory.");
    }
}

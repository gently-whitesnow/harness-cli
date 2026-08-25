using System.Runtime.InteropServices;

namespace Harness.Tests;

/// <summary>Publishes one native executable shared by distribution-level tests.</summary>
public static class NativePublication
{
    private static readonly Lazy<(string Executable, string BuildOutput)> Publication =
        new(Publish, LazyThreadSafetyMode.ExecutionAndPublication);

    public static (string Executable, string BuildOutput) Get() => Publication.Value;

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
            Release.RepositoryRoot());

        Assert.True(publication.ExitCode == 0, "NativeAOT publication failed:\n" + publication.Output);

        var executable = Path.Combine(outputDirectory, "harness");
        Assert.True(File.Exists(executable), "Published executable not found:\n" + publication.Output);
        return (executable, publication.Output);
    }
}

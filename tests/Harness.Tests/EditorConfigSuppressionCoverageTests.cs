namespace Harness.Tests;

public sealed class EditorConfigSuppressionCoverageTests
{
    private const string Check = "warning-suppressions.dotnet";

    [Theory]
    [InlineData("src/**/*.cs", "src/Widget.cs", 1)]
    [InlineData("src/**/*.cs", "src/Nested/Widget.cs", 1)]
    [InlineData("src/**/*.cs", "other/Widget.cs", 0)]
    [InlineData("src/[!A]*.cs", "src/Widget.cs", 1)]
    [InlineData("src/[!A]*.cs", "src/App.cs", 0)]
    public void Local_section_is_judged_when_editorconfig_glob_covers_a_source(
        string pattern,
        string source,
        int expected)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(".editorconfig", $"root = true\n[{pattern}]\ndotnet_diagnostic.CA1707.severity = none\n")
            .WriteFile("src/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile(source, Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(expected, run.ExitCode);
        if (expected == 1)
        {
            Assert.Contains($"silences CA1707 via [{pattern}]", run.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("silences CA1707", run.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("vbproj", "vb", "Public Class Widget\nEnd Class\n")]
    [InlineData("fsproj", "fs", "module Widget\nlet value = 1\n")]
    [InlineData("fsproj", "fsi", "module Widget\nval value: int\n")]
    public void Local_section_addressing_another_dotnet_language_is_reported(
        string projectExtension,
        string sourceExtension,
        string source)
    {
        var pattern = $"src/*.{sourceExtension}";
        using var repository = Fixtures.Compliant()
            .WriteFile(".editorconfig", $"root = true\n[{pattern}]\ndotnet_diagnostic.CA1707.severity = none\n")
            .WriteFile($"src/App.{projectExtension}", Fixtures.SimpleSdkProject)
            .WriteFile($"src/Widget.{sourceExtension}", source)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.Contains($"silences CA1707 via [{pattern}]", run.Output, StringComparison.Ordinal);
    }
}

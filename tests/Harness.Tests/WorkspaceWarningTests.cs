namespace Harness.Tests;

public sealed class WorkspaceWarningTests
{
    [Theory]
    [InlineData("apps/two/Legacy.cs", 0)]
    [InlineData("apps/one/Code.cs", 1)]
    public void Shared_editorconfig_only_reports_sections_covering_owned_sources(string pattern, int expected)
    {
        using var repository = Workspace()
            .WriteFile(".editorconfig", $"root = true\n[{pattern}]\ndotnet_diagnostic.CS0168.severity = none\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/one");

        Assert.Equal(expected, run.ExitCode);
        if (expected == 1)
        {
            Assert.Contains("silences CS0168", run.Output, StringComparison.Ordinal);
            Assert.Contains("../../.editorconfig", run.Output, StringComparison.Ordinal);
        }
        else
        {
            Assert.DoesNotContain("silences CS0168", run.Output, StringComparison.Ordinal);
        }
    }

    [Theory]
    [InlineData("apps/one/.editorconfig")]
    [InlineData("apps/.editorconfig")]
    public void Nearest_root_editorconfig_stops_ancestor_suppression_evidence(string rootConfig)
    {
        using var repository = Workspace()
            .WriteFile(".editorconfig", "root = true\n[apps/one/Code.cs]\ndotnet_diagnostic.CS0168.severity = none\n")
            .WriteFile(rootConfig, "root = true\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/one");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("silences CS0168", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Shared_repository_wide_suppression_remains_visible()
    {
        using var repository = Workspace()
            .WriteFile(".editorconfig", "root = true\n[*.cs]\ndotnet_diagnostic.CS0168.severity = none\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/one");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("CS0168 is switched off repository-wide", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_section_addressing_a_project_is_judged_only_in_that_project()
    {
        using var repository = Fixtures.WithRawFrame("""
                {"version":"latest","projects":["apps/one"],
                 "settings":{"commits":{"language":"ru","requireSetup":false}},
                 "applicability":{"dotnet":{"applicable":true}},
                 "policy":{"warning-suppressions.dotnet":"required"}}
                """)
            .WriteFile(".editorconfig", "root = true\n[apps/one/Code.cs]\ndotnet_diagnostic.CS0168.severity = none\n")
            .WriteFile("Tool.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("Tool.cs", "public class Tool {}\n")
            .WriteFile("apps/one/.harness.json", """
                {"settings":{},"applicability":{"dotnet":{"applicable":true}},
                 "policy":{"warning-suppressions.dotnet":"required"}}
                """)
            .WriteFile("apps/one/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("apps/one/Code.cs", "public class Code {}\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        var scopes = run.Output.Split("Scope: ");
        Assert.Equal(3, scopes.Length);
        Assert.DoesNotContain("silences CS0168", scopes[1], StringComparison.Ordinal);
        Assert.Contains("silences CS0168", scopes[2], StringComparison.Ordinal);
    }

    [Fact]
    public void Only_the_nearest_build_props_speaks_for_a_project()
    {
        using var repository = Workspace()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "    <Deterministic>true</Deterministic>",
                "    <Deterministic>true</Deterministic>\n    <NoWarn>$(NoWarn);CS1591</NoWarn>",
                StringComparison.Ordinal))
            .WriteFile("apps/one/Directory.Build.props", Fixtures.HardenedBuildProps)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/one");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("CS1591 is switched off", run.Output, StringComparison.Ordinal);
    }

    private static RepositoryFixture Workspace()
        => Fixtures.WithRawFrame("""
            {"version":"latest","projects":["apps/one","apps/two"],
             "settings":{"commits":{"language":"ru","requireSetup":false}},"policy":{}}
            """)
            .WriteFile("apps/one/.harness.json", """
                {"settings":{},"applicability":{"dotnet":{"applicable":true}},
                 "policy":{"warning-suppressions.dotnet":"required"}}
                """)
            .WriteFile("apps/one/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("apps/one/Code.cs", "public class Code {}\n")
            .WriteFile("apps/two/.harness.json", """{"settings":{},"policy":{}}""")
            .WriteFile("apps/two/Legacy.cs", "public class Legacy {}\n");
}

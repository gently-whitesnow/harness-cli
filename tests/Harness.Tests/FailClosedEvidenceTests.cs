namespace Harness.Tests;

public sealed class FailClosedEvidenceTests
{
    [Theory]
    [InlineData("<Import Project=\"https://example.invalid/weak.props\" />", "Import")]
    [InlineData("<PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup>", "RunAnalyzers")]
    [InlineData("<PropertyGroup><WarningLevel>0</WarningLevel></PropertyGroup>", "WarningLevel")]
    [InlineData("<PropertyGroup><AnalysisModeSecurity>None</AnalysisModeSecurity></PropertyGroup>", "AnalysisModeSecurity")]
    [InlineData("<PropertyGroup><CodeAnalysisTreatWarningsAsErrors>false</CodeAnalysisTreatWarningsAsErrors></PropertyGroup>", "CodeAnalysisTreatWarningsAsErrors")]
    [InlineData("<Import Project=\"Local.props\" Condition=\"'$(Configuration)' == 'Debug'\" />", "conditional Import")]
    public void Build_properties_fail_closed_on_unreviewed_weakening(string xml, string expected)
    {
        var props = Fixtures.HardenedBuildProps.Replace("</Project>", xml + "</Project>", StringComparison.Ordinal);
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", props)
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains(expected, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Targets_cannot_weaken_analyzer_coverage()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("Directory.Build.targets", "<Project><PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup></Project>")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("RunAnalyzers", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Tracked_local_import_is_inspected()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "</Project>", "<Import Project=\"Local.props\" /></Project>", StringComparison.Ordinal))
            .WriteFile("Local.props", "<Project><PropertyGroup><RunAnalyzers>false</RunAnalyzers></PropertyGroup></Project>")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("RunAnalyzers", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Non_sdk_project_is_reported()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", "<Project><PropertyGroup><TargetFramework>net10.0</TargetFramework></PropertyGroup></Project>")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "build-properties.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("non-SDK-style", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unknown_nowarn_property_is_reported()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "</Project>", "<PropertyGroup><NoWarn>$(SharedWarnings)</NoWarn></PropertyGroup></Project>", StringComparison.Ordinal))
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("unresolved MSBuild property", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Nullable_disable_is_an_addressed_suppression()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", "#nullable " + "disable\n" + Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("#nullable disable", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Global_analyzer_config_cannot_hide_a_diagnostic()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .WriteFile("analyzers.globalconfig", "is_global = true\n[*.cs]\ndotnet_diagnostic.CA1707.severity = none\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("CA1707", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Rule_set_reference_is_reported()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject.Replace(
                "</Project>", "<PropertyGroup><CodeAnalysisRuleSet>rules.ruleset</CodeAnalysisRuleSet></PropertyGroup></Project>", StringComparison.Ordinal))
            .WriteFile("rules.ruleset", "<RuleSet Name=\"test\" ToolsVersion=\"15.0\" />")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("CodeAnalysisRuleSet", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Assembly_suppress_message_is_an_addressed_suppression()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", "[assembly: System.Diagnostics.CodeAnalysis.SuppressMessage(\"Naming\", \"CA1707\")]\n" + Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "warning-suppressions.dotnet");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("SuppressMessage", run.Output, StringComparison.Ordinal);
    }
}

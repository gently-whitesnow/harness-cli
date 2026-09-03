namespace Harness.Tests;

public sealed class WarningSuppressionsTests
{
    private const string Check = "warning-suppressions.dotnet";

    // Assembled from pieces so that this test file does not itself carry the suppressions
    // the check reads; the harness judges its own tests too.
    private static readonly string SuppressingSource = string.Join('\n',
        "using System.Diagnostics.CodeAnalysis;",
        "#pragma warning " + "disable 8600, CA1822 // legacy",
        "namespace App;",
        "",
        "public static class Widget",
        "{",
        "    [Suppress" + "Message(\"Design\", \"CA1000:Do not declare static members\", Justification = \"fixture\")]",
        "    public static int Size() => 1;",
        "}",
        "");

    [Fact]
    public void Pragma_attribute_project_nowarn_and_scoped_severity_are_each_named()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", "root = true\n[tests/**/*.cs]\ndotnet_diagnostic.CA1707.severity = none\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject.Replace(
                "    <TargetFramework>net10.0</TargetFramework>",
                "    <TargetFramework>net10.0</TargetFramework>\n    <NoWarn>$(NoWarn);CS1591</NoWarn>\n    <WarningsNotAsErrors>CS0618</WarningsNotAsErrors>",
                StringComparison.Ordinal))
            .WriteFile("src/App/Widget.cs", SuppressingSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check, "--all");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/Widget.cs:2") && run.OutputContains("silences CS8600 via #pragma warning disable"), run.Output);
        Assert.True(run.OutputContains("silences CA1822 via #pragma warning disable"), run.Output);
        Assert.True(run.OutputContains("src/App/Widget.cs:7") && run.OutputContains("silences CA1000 via SuppressMessage"), run.Output);
        Assert.True(run.OutputContains("src/App/App.csproj:4") && run.OutputContains("silences CS1591 via NoWarn at one address"), run.Output);
        Assert.True(run.OutputContains("silences CS0618 via WarningsNotAsErrors"), run.Output);
        Assert.True(run.OutputContains(".editorconfig:3") && run.OutputContains("silences CA1707 via [tests/**/*.cs] severity = none"), run.Output);
        Assert.True(run.OutputContains("switch CS1591 off for the whole repository"), run.Output);
    }

    [Fact]
    public void Blanket_pragma_and_category_severity_fail_even_repository_wide()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", "root = true\n[*.cs]\ndotnet_analyzer_diagnostic.category-Style.severity = none\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", "#pragma warning " + "disable\n" + Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("silences every warning via #pragma warning disable"), run.Output);
        Assert.True(run.OutputContains("silences every warning via dotnet_analyzer_diagnostic.category-style.severity = none"), run.Output);
    }

    [Fact]
    public void Repository_wide_switches_pass_and_are_printed_on_every_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps.Replace(
                "    <Deterministic>true</Deterministic>",
                "    <Deterministic>true</Deterministic>\n    <NoWarn>$(NoWarn);CS1591</NoWarn>",
                StringComparison.Ordinal))
            .WriteFile(".editorconfig", "root = true\n[*.{cs,vb}]\ndotnet_diagnostic.CA1707.severity = none\n[*]\ndotnet_diagnostic.CA1716.severity = suggestion\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("CA1707 is switched off repository-wide via [*.{cs,vb}] severity = none at .editorconfig:3"), run.Output);
        Assert.True(run.OutputContains("CA1716 is switched off repository-wide via [*] severity = suggestion at .editorconfig:5"), run.Output);
        Assert.True(run.OutputContains("CS1591 is switched off repository-wide via NoWarn at Directory.Build.props:10"), run.Output);
        Assert.True(run.OutputContains("no diagnostic is silenced at an address"), run.Output);
    }

    [Fact]
    public void Generated_sources_and_generated_code_sections_are_not_judged()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", "root = true\n[*.g.cs]\ngenerated_code = true\ndotnet_diagnostic.IDE0130.severity = none\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Client.g.cs", "#pragma warning " + "disable 108\nnamespace App;\npublic class Client { }\n")
            .WriteFile("src/App/Marked.cs", "// <auto-generated/>\n#pragma warning " + "disable 108\nnamespace App;\npublic class Marked { }\n")
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Name_prefix_section_without_generated_marker_is_an_address()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", "root = true\n[*.g.cs]\ndotnet_diagnostic.IDE0130.severity = none\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("silences IDE0130 via [*.g.cs] severity = none at one address"), run.Output);
    }
}

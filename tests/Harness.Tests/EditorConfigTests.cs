namespace Harness.Tests;

public sealed class EditorConfigTests
{
    private const string Check = "editorconfig.dotnet";

    [Fact]
    public void Project_without_editorconfig_fails()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/App/App.csproj"), run.Output);
        Assert.True(run.OutputContains("is not covered by a tracked .editorconfig"), run.Output);
    }

    [Fact]
    public void Reference_editorconfig_from_init_satisfies_the_check()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();
        var explained = HarnessCli.Run(repository.Path, "explain", Check);
        Assert.Equal(0, explained.ExitCode);
        var reference = ReferenceFileFrom(explained.StandardOutput);
        repository.WriteFile(".editorconfig", reference).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("every project reads the shared code-style baseline"), run.Output);
    }

    [Fact]
    public void Short_editorconfig_names_every_missing_key_once()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", "root = true\n\n[*.cs]\nindent_size = 4\ndotnet_diagnostic.IDE0055.severity = warning\n")
            .WriteFile("src/One/One.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("src/Two/Two.csproj", Fixtures.SimpleSdkProject.Replace("net10.0", "net9.0", StringComparison.Ordinal))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check, "--all");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("must set csharp_prefer_braces = true"), run.Output);
        Assert.True(run.OutputContains("must set dotnet_diagnostic.ide0161.severity = warning"), run.Output);
        Assert.Equal(1, Occurrences(run.Output, "must set csharp_prefer_braces = true"));
    }

    [Fact]
    public void Nearest_section_overrides_and_conflicting_value_is_named()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", Reference())
            .WriteFile("src/.editorconfig", "[*.cs]\ncsharp_style_namespace_declarations = block_scoped:warning\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/.editorconfig"), run.Output);
        Assert.True(
            run.OutputContains("sets csharp_style_namespace_declarations to 'block_scoped:warning' instead of file_scoped"),
            run.Output);
    }

    [Fact]
    public void Root_marker_below_the_reference_hides_it()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile(".editorconfig", Reference())
            .WriteFile("src/.editorconfig", "root = true\n[*.cs]\nindent_size = 4\n")
            .WriteFile("src/App/App.csproj", Fixtures.SimpleSdkProject)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/.editorconfig"), run.Output);
        Assert.True(run.OutputContains("must set end_of_line = lf"), run.Output);
    }

    private static string Reference()
    {
        using var scratch = Fixtures.Compliant();
        return ReferenceFileFrom(HarnessCli.Run(scratch.Path, "explain", Check).StandardOutput);
    }

    /// <summary>The indented reference file `explain` prints, de-indented back to a file.</summary>
    internal static string ReferenceFileFrom(string explanation)
    {
        var start = explanation.IndexOf("Reference .editorconfig\n", StringComparison.Ordinal);
        var end = explanation.IndexOf("\nApplicability", start, StringComparison.Ordinal);
        Assert.True(start >= 0 && end > start, explanation);
        var body = explanation[(start + "Reference .editorconfig\n".Length)..end];
        return string.Join('\n', body.Split('\n').Select(line => line.StartsWith("  ", StringComparison.Ordinal) ? line[2..] : line)) + "\n";
    }

    private static int Occurrences(string text, string needle)
    {
        var count = 0;
        for (var index = text.IndexOf(needle, StringComparison.Ordinal); index >= 0; index = text.IndexOf(needle, index + 1, StringComparison.Ordinal))
        {
            count++;
        }

        return count;
    }
}

namespace Harness.Tests;

public sealed class EvidenceScopeTests
{
    [Fact]
    public void Tracked_build_directory_is_measured_as_authored_typescript()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/pages/build/Page.ts", string.Concat(Enumerable.Repeat("// prose\n", 10)) + "export const page = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "comments.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("src/pages/build/Page.ts", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_generated_source_needs_both_path_and_marker()
    {
        var frame = Frame.AllPresent().Generated("""[{"paths":["src/Generated"],"reason":"generator output is tracked"}]""");
        using var repository = Fixtures.Compliant(frame)
            .WriteFile("src/Generated/Client.g.cs", "// generated\n")
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", "harness.coverage,comments.csharp");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("DeclaredGenerated: src/Generated/Client.g.cs", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Undeclared_marker_is_measured_and_reported()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/Generated/Client.g.cs", "// generated\n")
            .WriteFile("src/App/Widget.cs", Fixtures.FormattedSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("outside 'generated'", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Stale_generated_declaration_blocks_coverage()
    {
        var frame = Frame.AllPresent().Generated("""[{"paths":["src/Deleted"],"reason":"former generator"}]""");
        using var repository = Fixtures.Compliant(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("generated path 'src/Deleted' has no tracked files", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Declared_applicable_stack_without_sources_blocks_coverage()
    {
        using var repository = Fixtures.WithRawFrame(Frame.AllPresent().ToString());

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.Contains("applicability.csharp is true, but no tracked authored sources", run.Output, StringComparison.Ordinal);
    }
}

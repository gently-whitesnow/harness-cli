namespace Harness.Tests;

public sealed class TypesPerFileTests
{
    [Fact]
    public void Two_top_level_classes_or_records_fail()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(
                "src/Pair.cs",
                """
                namespace App;

                public sealed class First;
                public sealed record Second(int Value);
                """)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "types-per-file.csharp");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/Pair.cs"), run.Output);
        Assert.True(run.OutputContains("First"), run.Output);
        Assert.True(run.OutputContains("Second"), run.Output);
    }

    [Fact]
    public void Nested_types_and_other_top_level_type_forms_do_not_count()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(
                "src/Container.cs",
                """
                namespace App;

                public interface IContainer;
                public readonly struct Value;

                public sealed class Container
                {
                    private sealed class Detail;
                    private sealed record State(int Value);
                }
                """)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "types-per-file.csharp");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Csharp_applicability_disables_every_csharp_check_together()
    {
        using var repository = Fixtures.Compliant(
                Frame.AllPresent().NotApplicableTo("csharp", "repository contains vendored C# only"))
            .WriteFile("src/Pair.cs", "sealed class First; sealed class Second;")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "csharp");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("outcome: failed", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.Equal(6, run.Output.Split("outcome: not applicable", StringSplitOptions.None).Length - 1);
        Assert.True(run.OutputContains("vendored C# only"), run.Output);
    }
}

namespace Harness.Tests;

public sealed class CohesionTests
{
    private const string Check = "cohesion.csharp";

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void A_type_holding_two_groups_that_share_no_state_is_reported()
    {
        using var repository = SourceRepository("src/App/Mixed.cs", CSharp.TwoGroups);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("independent member groups 2 exceeds"), run.Output);
        Assert.True(run.OutputContains("App.Mixed"), run.Output);
    }

    [Fact]
    public void A_finding_here_is_advisory_and_does_not_fail_the_run()
    {
        using var repository = SourceRepository("src/App/Mixed.cs", CSharp.TwoGroups);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void A_type_whose_members_all_reach_the_same_state_is_not_reported()
    {
        using var repository = SourceRepository("src/App/Counter.cs", CSharp.OneGroup);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("independent member groups"), run.Output);
    }

    [Fact]
    public void A_type_that_holds_no_state_is_not_measured()
    {
        using var repository = SourceRepository("src/App/Helpers.cs", CSharp.NoState);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("independent member groups"), run.Output);
    }

    [Fact]
    public void A_type_with_too_few_members_says_nothing_either_way()
    {
        using var repository = SourceRepository("src/App/Pair.cs", CSharp.TwoSmallGroups);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("independent member groups"), run.Output);
    }

    [Fact]
    public void The_number_of_members_a_type_must_have_is_configurable()
    {
        using var repository = Fixtures
            .Compliant(Frame.AllPresent().Settings("""{ "cohesion.csharp": { "minimumMembers": 3 } }"""))
            .WriteFile("src/App/Pair.cs", CSharp.TwoSmallGroups)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("independent member groups 2 exceeds"), run.Output);
    }

    [Fact]
    public void Explain_states_the_formula_what_it_leaves_out_and_its_limits()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Formula"), run.Output);
        Assert.True(run.OutputContains("Constructors are left out"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
    }

    private static RepositoryFixture SourceRepository(string path, string source)
        => Fixtures.Compliant(Frame.AllPresent()).WriteFile(path, source).Commit();

    private static class CSharp
    {
        public const string TwoGroups =
            """
        namespace App;

        public sealed class Mixed
        {
            private int left;
            private int right;

            public int AddLeft(int value) => left + value;

            public int ReadLeft() => left;

            public int AddRight(int value) => right + value;

            public int ReadRight() => right;
        }

        """;

        public const string OneGroup =
            """
        namespace App;

        public sealed class Counter
        {
            private int total;
            private int steps;

            public int Add(int value) => total + value;

            public int Read() => total;

            public int Step() => steps + Read();

            public int Steps() => steps;
        }

        """;

        public const string NoState =
            """
        namespace App;

        public static class Helpers
        {
            public static int First(int value) => value + 1;

            public static int Second(int value) => value + 2;

            public static int Third(int value) => value + 3;

            public static int Fourth(int value) => value + 4;

            public static int Fifth(int value) => value + 5;

            public static int Sixth(int value) => value + 6;
        }

        """;

        public const string TwoSmallGroups =
            """
        namespace App;

        public sealed class Pair
        {
            private int left;
            private int right;

            public int ReadLeft() => left;

            public int ReadRight() => right;
        }

        """;
    }
}

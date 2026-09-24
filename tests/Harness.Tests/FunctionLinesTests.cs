namespace Harness.Tests;

public sealed class FunctionLinesTests
{
    [Fact]
    public void Csharp_data_literals_switch_expression_and_raw_string_do_not_inflate_own_lines()
    {
        var values = string.Join("\n", Enumerable.Range(1, 24).Select(number => $"            {number},"));
        var source = $$""""
            class Runner
            {
                int Run(int key)
                {
                    int[] values = [
            {{values}}
                    ];
                    var message = """
                        first
                        second
                        third
                        fourth
                        """;
                    var selected = key switch
                    {
                        1 => values[0],
                        2 => values[1],
                        _ => values[2],
                    };
                    return selected + message.Length;
                }
            }
            """";
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Settings("""{ "functions.csharp": { "ownLines": 11 } }"""))
            .WriteFile("Runner.cs", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "functions.csharp");

        Assert.True(run.ExitCode == 0, run.Output);
    }

    [Fact]
    public void Csharp_top_level_statements_are_measured_and_ef_migrations_are_generated()
    {
        var statements = string.Join("\n", Enumerable.Range(1, 12).Select(number => $"Console.WriteLine({number});"));
        var migration = """
            using Microsoft.EntityFrameworkCore.Migrations;
            [Migration("20260924_Create")]
            class Create : Migration
            {
                void Up()
                {
                    Console.WriteLine("generated");
                }
            }
            """;
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Settings("""{ "functions.csharp": { "ownLines": 10 } }"""))
            .WriteFile("Program.cs", statements)
            .WriteFile("Create.cs", migration).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "functions.csharp");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Program.cs:1: <top-level> owns"), run.Output);
        Assert.False(run.OutputContains("Create.cs"), run.Output);
    }

    [Fact]
    public void Csharp_nested_lambda_and_local_function_are_measured_separately()
    {
        var source = """
            class Runner
            {
                void Run()
                {
                    Action work = () =>
                    {
                        var value = 0;
                        value++;
                        value++;
                        value++;
                        value++;
                    };
                    void Step()
                    {
                        var value = 0;
                        value++;
                        value++;
                        value++;
                        value++;
                    }
                    Step();
                    work();
                }
            }
            """;
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Settings("""{ "functions.csharp": { "ownLines": 6 } }"""))
            .WriteFile("Runner.cs", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "functions.csharp");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Step owns"), run.Output);
        Assert.True(run.OutputContains("<lambda> owns"), run.Output);
        Assert.False(run.OutputContains("Run owns"), run.Output);
    }

    [Fact]
    public void Expression_bodied_csharp_method_and_lambda_are_separate_units()
    {
        var source = """
            class Runner
            {
                int Transform(int x) => x
                    + 1
                    + 2
                    + 3
                    + 4
                    + 5
                    + 6;

                void Run()
                {
                    Func<int, int> map = x => x
                        + 1
                        + 2
                        + 3
                        + 4
                        + 5
                        + 6;
                    _ = map(1);
                }
            }
            """;
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Settings("""{ "functions.csharp": { "ownLines": 6 } }"""))
            .WriteFile("Runner.cs", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "functions.csharp");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("Transform owns"), run.Output);
        Assert.True(run.OutputContains("<lambda> owns"), run.Output);
        Assert.False(run.OutputContains("Run owns"), run.Output);
    }

    [Fact]
    public void Go_composite_literal_lines_do_not_count_but_function_literal_does()
    {
        var source = """
            package sample
            func Run() {
                values := []int{
                    1,
                    2,
                    3,
                    4,
                    5,
                    6,
                }
                _ = values
                work := func() {
                    println(1)
                    println(2)
                    println(3)
                    println(4)
                    println(5)
                    println(6)
                    println(7)
                }
                work()
            }
            """;
        using var repository = Fixtures.Compliant(Frame.AllPresent()
                .Settings("""{ "functions.go": { "ownLines": 7 } }"""))
            .WriteFile("run.go", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "functions.go");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("<func literal> owns"), run.Output);
        Assert.False(run.OutputContains("Run owns"), run.Output);
    }
}

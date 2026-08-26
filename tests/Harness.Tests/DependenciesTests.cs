using System.Text;

namespace Harness.Tests;

public sealed class DependenciesTests
{
    private const string Check = "dependencies.csharp";

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void A_cycle_proved_by_declaration_positions_fails_the_run()
    {
        using var repository = Cycle();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("module dependency cycle"), run.Output);
        Assert.True(run.OutputContains("App.Left -> App.Right -> App.Left"), run.Output);
        Assert.True(run.OutputContains("App.Left.Service names App.Right.Store"), run.Output);
    }

    [Fact]
    public void A_cycle_that_only_inferred_references_close_does_not_fail_the_run()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/Left/Service.cs", CSharp.Uses("App.Left", "Service", "App.Right", "Store"))
            .WriteFile("src/App/Right/Store.cs", CSharp.Mentions("App.Right", "Store", "App.Left", "Service"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("module dependency cycle"), run.Output);
    }

    [Fact]
    public void A_namespace_and_the_namespace_inside_it_are_one_module_and_not_a_cycle()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/Registry.cs", CSharp.Uses("App", "Registry", "App.Parts", "Widget"))
            .WriteFile("src/App/Parts/Widget.cs", CSharp.Uses("App.Parts", "Widget", "App", "Contract"))
            .WriteFile("src/App/Contract.cs", CSharp.Empty("App", "Contract"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("module dependency cycle"), run.Output);
    }

    [Fact]
    public void Current_contract_does_not_report_dependency_counts()
    {
        using var repository = Fixtures
            .Compliant()
            .WriteFile("src/App/Env.cs", "namespace App; public enum Env { Production }\n")
            .WriteFile(
                "src/App/Model.cs",
                "namespace App; public sealed class Model { public Env Current { get; init; } }\n")
            .WriteFile(
                "src/App/Named.cs",
                "namespace App; public sealed class Named { public string Env { get; init; } = string.Empty; }\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("type references"), run.Output);
        Assert.False(run.OutputContains("external import fan-out"), run.Output);
    }

    [Fact]
    public void The_share_of_names_that_resolved_is_reported_with_the_result()
    {
        using var repository = Cycle();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("resolved to exactly one of them"), run.Output);
    }

    [Fact]
    public void Explain_limits_the_current_check_to_proved_cycles()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Evidence"), run.Output);
        Assert.True(run.OutputContains("Why a cycle is blocking"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("Policy"), run.Output);
        Assert.True(run.OutputContains("does not report fan-in or fan-out"), run.Output);
    }

    private static RepositoryFixture Cycle(Frame? frame = null)
        => Fixtures.Compliant(frame ?? Frame.AllPresent())
            .WriteFile("src/App/Left/Service.cs", CSharp.Uses("App.Left", "Service", "App.Right", "Store"))
            .WriteFile("src/App/Right/Store.cs", CSharp.Uses("App.Right", "Store", "App.Left", "Service"))
            .Commit();

    private static class CSharp
    {
        /// <summary>A type whose field declares another type: a position only a type can hold.</summary>
        public static string Uses(string module, string name, string imported, string used)
            => $$"""
            using {{imported}};

            namespace {{module}};

            public sealed class {{name}}
            {
                private {{used}}? held;

                public object? Held() => held;
            }

            """;

        /// <summary>A type that names another only through a member access: never proof.</summary>
        public static string Mentions(string module, string name, string imported, string used)
            => $$"""
            using {{imported}};

            namespace {{module}};

            public sealed class {{name}}
            {
                public static string Describe() => {{used}}.Label;

                public const string Label = "x";
            }

            """;

        public static string Empty(string module, string name)
            => $$"""
            namespace {{module}};

            public sealed class {{name}}
            {
                public const string Label = "x";
            }

            """;

        public static string ManyImports(int imports)
        {
            var text = new StringBuilder();
            for (var index = 0; index < imports; index++)
            {
                text.Append("using Outside.Area").Append(index).Append(";\n");
            }

            return text.Append("\nnamespace App;\n\npublic static class Imports\n{\n}\n").ToString();
        }

        public const string UsingStatements =
            """
        using System.IO;

        namespace App;

        public static class Reader
        {
            public static string Read(string path)
            {
                using var reader = new StreamReader(path);
                using (var second = new StreamReader(path))
                {
                    return reader.ReadToEnd() + second.ReadToEnd();
                }
            }
        }

        """;
    }
}

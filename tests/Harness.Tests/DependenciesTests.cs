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
    public void A_proved_cycle_can_be_accepted_in_writing_by_naming_the_file()
    {
        using var repository = Cycle(
            Frame.AllPresent().Suppressing(Check, "src/App/Left/Service.cs", "one release away from split"));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("suppressed"), run.Output);
        Assert.True(run.OutputContains("one release away from split"), run.Output);
    }

    [Fact]
    public void Import_fan_out_counts_imports_from_outside_the_repository()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Policy(Check, "advisory"))
            .WriteFile("src/App/Imports.cs", CSharp.ManyImports(25))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("external import fan-out 25 exceeds"), run.Output);
    }

    [Fact]
    public void An_import_of_a_namespace_this_repository_declares_is_not_external()
    {
        using var repository = Fixtures
            .Compliant(Frame.AllPresent().Settings("""{ "dependencies.csharp": { "externalImports": 0 } }"""))
            .WriteFile("src/App/Registry.cs", CSharp.Uses("App", "Registry", "Other", "Widget"))
            .WriteFile("src/Other/Widget.cs", CSharp.Empty("Other", "Widget"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("external import fan-out"), run.Output);
    }

    [Fact]
    public void A_using_statement_inside_a_method_is_not_an_import()
    {
        using var repository = Fixtures
            .Compliant(Frame.AllPresent().Settings("""{ "dependencies.csharp": { "externalImports": 1 } }"""))
            .WriteFile("src/App/Stream.cs", CSharp.UsingStatements)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
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
    public void Explain_separates_what_is_proved_from_what_is_estimated()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Evidence"), run.Output);
        Assert.True(run.OutputContains("Why a cycle is blocking"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("Possible damage"), run.Output);
        Assert.True(run.OutputContains("not semantic coupling"), run.Output);
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

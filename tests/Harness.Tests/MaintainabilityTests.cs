using System.Text;

namespace Harness.Tests;

/// <summary>
/// What the maintainability gate measures, what it refuses to claim, and how it stays
/// advisory. The fixtures are C# sources without a project file, so the gate is exercised
/// on its own analysis rather than through the SDK.
/// </summary>
public sealed class MaintainabilityTests
{
    private const string Check = "maintainability.csharp";

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.False(run.OutputContains("passed"), run.Output);
    }

    [Fact]
    public void A_long_method_is_reported_with_its_metric_value_comparison_point_and_location()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongMethod(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(Check), run.Output);
        Assert.True(run.OutputContains("method logical lines 75 exceeds"), run.Output);
        Assert.True(run.OutputContains("comparison point of 60"), run.Output);
        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.True(run.OutputContains("src/App/Report.cs:5"), run.Output);
    }

    /// <summary>
    /// A parenthesized group before the member name is a return tuple. Reading it as the
    /// parameter list named members after their last modifier and counted the wrong arity;
    /// the harness found this against its own source.
    /// </summary>
    [Fact]
    public void A_tuple_returning_method_is_named_after_the_member_and_not_its_modifier()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongTupleReturningMethod(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.False(run.OutputContains("App.Report.static"), run.Output);
        Assert.False(run.OutputContains("constructor parameter count"), run.Output);
    }

    [Fact]
    public void Maintainability_findings_are_advisory_and_do_not_fail_the_run()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongMethod(70));

        var run = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void A_multiline_signature_is_attributed_to_the_line_the_declaration_starts_on()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongMethodWithSplitSignature(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("src/App/Report.cs:5"), run.Output);
    }

    [Fact]
    public void Branch_tokens_inside_comments_strings_and_char_literals_are_not_control_flow()
    {
        using var repository = SourceRepository("src/App/Quiet.cs", CSharp.BranchTokensOnlyInCommentsAndStrings);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/App/Quiet.cs"), run.Output);
    }

    [Fact]
    public void Real_control_flow_is_reported_as_a_lexical_branch_count()
    {
        using var repository = SourceRepository("src/App/Router.cs", CSharp.BranchingMethod);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("lexical branch count"), run.Output);
        Assert.True(run.OutputContains("App.Router.Route"), run.Output);
    }

    [Fact]
    public void A_record_primary_constructor_reports_its_parameter_count()
    {
        using var repository = SourceRepository("src/App/Money.cs", CSharp.WideRecord);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("constructor parameter count 7 exceeds"), run.Output);
        Assert.True(run.OutputContains("App.Money"), run.Output);
    }

    [Fact]
    public void A_declared_constructor_reports_its_parameter_count()
    {
        using var repository = SourceRepository("src/App/Service.cs", CSharp.WideConstructor);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("constructor parameter count"), run.Output);
        Assert.True(run.OutputContains("App.Service"), run.Output);
    }

    [Fact]
    public void A_wide_public_surface_is_reported_for_the_declaring_type()
    {
        using var repository = SourceRepository("src/App/Facade.cs", CSharp.WidePublicSurface(30));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("public declared members 30 exceeds"), run.Output);
        Assert.True(run.OutputContains("App.Facade"), run.Output);
    }

    [Fact]
    public void Import_fan_out_counts_using_directives_and_not_using_statements()
    {
        using var repository = SourceRepository("src/App/Imports.cs", CSharp.ManyUsingDirectives(25));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("using directive fan-out 25 exceeds"), run.Output);
    }

    [Fact]
    public void A_using_statement_inside_a_method_is_not_an_import()
    {
        using var repository = SourceRepository("src/App/Stream.cs", CSharp.UsingStatements);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("using directive fan-out"), run.Output);
    }

    [Fact]
    public void A_large_file_and_a_large_type_are_reported_separately()
    {
        using var repository = SourceRepository("src/App/Big.cs", CSharp.LargeType(420));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("file logical lines"), run.Output);
        Assert.True(run.OutputContains("type logical lines"), run.Output);
        Assert.True(run.OutputContains("App.Big"), run.Output);
    }

    [Fact]
    public void A_nested_type_is_named_through_its_enclosing_type()
    {
        using var repository = SourceRepository("src/App/Outer.cs", CSharp.NestedTypeWithLongMethod(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Outer.Inner.Compute"), run.Output);
    }

    [Fact]
    public void Expression_bodied_members_and_records_do_not_confuse_the_reader()
    {
        using var repository = SourceRepository("src/App/Compact.cs", CSharp.ExpressionBodiedMembers);

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/App/Compact.cs"), run.Output);
    }

    /// <summary>
    /// Quotes and braces inside an interpolation hole once ended the literal early, which
    /// silently lost every declaration after it instead of reporting anything.
    /// </summary>
    [Fact]
    public void A_literal_with_awkward_interpolation_does_not_hide_what_follows_it()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongMethodAfterAwkwardLiterals(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.False(run.OutputContains("App.Report.Interpolate"), run.Output);
    }

    [Fact]
    public void Generated_and_build_output_sources_are_excluded()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/obj/Debug/Report.cs", CSharp.LongMethod(70))
            .WriteFile("src/App/Report.g.cs", CSharp.LongMethod(70))
            .WriteFile("src/App/Report.generated.cs", CSharp.LongMethod(70))
            .WriteFile("src/App/Report.Designer.cs", CSharp.LongMethod(70))
            .WriteFile("src/App/Marked.cs", "// <auto-generated />\n" + CSharp.LongMethod(70))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Untracked_sources_are_ignored()
    {
        using var repository = Fixtures.Compliant().WriteFile("src/App/Report.cs", CSharp.LongMethod(70));

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Many_findings_stay_bounded_and_still_report_how_many_there_are()
    {
        var repository = Fixtures.Compliant();
        for (var index = 0; index < 40; index++)
        {
            repository.WriteFile($"src/App/Report{index:00}.cs", CSharp.LongMethod(70));
        }

        using var committed = repository.Commit();

        var run = HarnessCli.Run(committed.Path, "check", "--only", Check);

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 20, run.Output);
        Assert.True(run.OutputContains("40"), run.Output);
    }

    [Fact]
    public void The_gate_reports_a_duration_and_leaves_the_repository_unchanged()
    {
        using var repository = SourceRepository("src/App/Report.cs", CSharp.LongMethod(70));
        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(" ms)"), run.Output);
        Assert.Equal(before, repository.TrackedState());
    }

    /// <summary>
    /// The metrics the copied Python script made useful, all reachable from one repository,
    /// so the behaviour worth keeping is fixed before that script is removed elsewhere.
    /// </summary>
    [Fact]
    public void Every_documented_metric_is_reachable_from_one_repository()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/Big.cs", CSharp.LargeType(420))
            .WriteFile("src/App/Report.cs", CSharp.LongMethod(70))
            .WriteFile("src/App/Router.cs", CSharp.BranchingMethod)
            .WriteFile("src/App/Money.cs", CSharp.WideRecord)
            .WriteFile("src/App/Facade.cs", CSharp.WidePublicSurface(30))
            .WriteFile("src/App/Imports.cs", CSharp.ManyUsingDirectives(25))
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        foreach (var metric in new[]
        {
            "file logical lines",
            "type logical lines",
            "method logical lines",
            "lexical branch count",
            "constructor parameter count",
            "public declared members",
            "using directive fan-out",
        })
        {
            Assert.True(run.OutputContains(metric), metric + " is missing from:\n" + run.Output);
        }
    }

    [Fact]
    public void Explain_states_the_formulas_the_limits_and_that_judgement_is_required()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Formulas"), run.Output);
        Assert.True(run.OutputContains("Limits"), run.Output);
        Assert.True(run.OutputContains("Possible damage"), run.Output);
        Assert.True(run.OutputContains("judgement"), run.Output);
    }

    [Fact]
    public void Explain_refuses_the_claims_the_metrics_do_not_support()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "explain", Check);

        Assert.True(run.OutputContains("not semantic coupling"), run.Output);
        Assert.True(run.OutputContains("not a dependency count"), run.Output);
        Assert.True(run.OutputContains("not a compiler control-flow graph"), run.Output);
    }

    private static RepositoryFixture SourceRepository(string path, string source)
        => Fixtures.Compliant().WriteFile(path, source).Commit();
}

/// <summary>C# shapes the maintainability metrics are measured against.</summary>
internal static class CSharp
{
    /// <summary>A method whose declaration starts on line 5 and whose body is long.</summary>
    public static string LongMethod(int statements)
        => $$"""
            namespace App;

            public static class Report
            {
                public static int Compute(int seed)
                {
                    var total = seed;
            {{Statements(statements)}}        return total;
                }
            }

            """;

    /// <summary>
    /// A long method returning a tuple, whose seven-element return type would read as an
    /// over-wide constructor if it were mistaken for a parameter list.
    /// </summary>
    public static string LongTupleReturningMethod(int statements)
        => $$"""
            namespace App;

            public static class Report
            {
                public static (int A, int B, int C, int D, int E, int F, int G) Compute(int seed)
                {
                    var total = seed;
            {{Statements(statements)}}        return (total, 0, 0, 0, 0, 0, 0);
                }
            }

            """;

    /// <summary>The same method with its signature split across three physical lines.</summary>
    public static string LongMethodWithSplitSignature(int statements)
        => $$"""
            namespace App;

            public static class Report
            {
                public static int Compute(
                    int seed,
                    int offset)
                {
                    var total = seed + offset;
            {{Statements(statements)}}        return total;
                }
            }

            """;

    /// <summary>
    /// Every interpolation shape whose holes carry quotes, braces or nested literals,
    /// followed by a method the reader must still find.
    /// </summary>
    public static string LongMethodAfterAwkwardLiterals(int statements)
        => $$$$""""
            namespace App;

            public static class Report
            {
                public static string Interpolate(int a, int b)
                {
                    var hole = $"{(a > b ? "left" : "right")} and {a switch { 0 => "zero", _ => "other" }}";
                    var nested = $"outer {Echo($"inner {a}")} end";
                    var escaped = $"{{literal braces}} and {a}";
                    var commented = $"{/* } " */ a}";
                    var verbatim = $@"{(a > b ? "x" : "y")} "" tail";
                    var raw = $$"""
                        {{a}} a literal { and a literal } and a " quote
                        """;
                    return hole + nested + escaped + commented + verbatim + raw;
                }

                private static string Echo(string value) => value;

                public static int Compute(int seed)
                {
                    var total = seed;
            {{{{Statements(statements)}}}}        return total;
                }
            }

            """";

    public static string NestedTypeWithLongMethod(int statements)
        => $$"""
            namespace App;

            public static class Outer
            {
                public static class Inner
                {
                    public static int Compute(int seed)
                    {
                        var total = seed;
            {{Statements(statements)}}            return total;
                    }
                }
            }

            """;

    /// <summary>
    /// Every branch token appears only inside a comment, a regular string, a verbatim
    /// string, a raw string or a character literal. A lexical reader that does not remove
    /// them would report this method as the most complex in the repository.
    /// </summary>
    public const string BranchTokensOnlyInCommentsAndStrings =
        """"
        namespace App;

        public static class Quiet
        {
            // if if if if if if if if && || ?? for foreach while case catch when
            /* if if if if if if if if && || ?? for foreach while case catch when
               if if if if if if if if && || ?? for foreach while case catch when */
            public static string Describe()
            {
                var regular = "if (a && b) { for (;;) { } } catch when case foreach ?? ||";
                var escaped = "a \" if if if && || ?? while { } catch";
                var verbatim = @"if if if "" && || ?? foreach case { } catch when";
                var raw = """
                    if (a || b) while (c) { case d: } catch when && ?? foreach
                    if (a || b) while (c) { case d: } catch when && ?? foreach
                    """;
                var brace = '{';
                var quote = '"';
                var backslash = '\\';
                return regular + escaped + verbatim + raw + brace + quote + backslash;
            }
        }

        """";

    /// <summary>Fifteen counted branch tokens plus the entry path.</summary>
    public const string BranchingMethod =
        """
        namespace App;

        public static class Router
        {
            public static int Route(int value, bool flag, string? name)
            {
                if (value < 0 && flag)
                {
                    return 0;
                }

                if (value > 10 || !flag)
                {
                    return 1;
                }

                foreach (var character in name ?? string.Empty)
                {
                    while (character == 'x' && flag)
                    {
                        value++;
                    }

                    for (var index = 0; index < 3 || flag; index++)
                    {
                        value--;
                    }
                }

                try
                {
                    switch (value)
                    {
                        case 1:
                            return 2;
                        case 2:
                            return 3;
                        default:
                            return 4;
                    }
                }
                catch (InvalidOperationException) when (flag)
                {
                    return -1;
                }
            }
        }

        """;

    public const string WideRecord =
        """
        namespace App;

        public sealed record Money(
            decimal Amount,
            string Currency,
            string Region,
            string Ledger,
            string Owner,
            string Category,
            string Note);

        """;

    public const string WideConstructor =
        """
        namespace App;

        public sealed class Service
        {
            public Service(
                string a,
                string b,
                string c,
                string d,
                string e,
                string f,
                string g)
            {
                Name = a + b + c + d + e + f + g;
            }

            public string Name { get; }
        }

        """;

    /// <summary>Expression-bodied members, a positional record and a lambda field.</summary>
    public const string ExpressionBodiedMembers =
        """
        namespace App;

        public sealed record Point(int X, int Y)
        {
            public int Sum => X + Y;

            public int Scaled(int factor) => Sum * factor;
        }

        public static class Compact
        {
            private static readonly System.Func<int, int> Double = value => value * 2;

            public static int Twice(int value) => Double(value);

            public static string Describe(int value) => value switch
            {
                0 => "zero",
                _ => "other",
            };
        }

        """;

    /// <summary>A method that opens disposables, which are statements rather than imports.</summary>
    public const string UsingStatements =
        """
        using System.IO;

        namespace App;

        public static class Stream
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

    public static string WidePublicSurface(int members)
    {
        var text = new StringBuilder("namespace App;\n\npublic sealed class Facade\n{\n");
        for (var index = 0; index < members; index++)
        {
            text.Append("    public int Member").Append(index).Append("() => ").Append(index).Append(";\n");
        }

        return text.Append("}\n").ToString();
    }

    public static string ManyUsingDirectives(int imports)
    {
        var text = new StringBuilder();
        for (var index = 0; index < imports; index++)
        {
            text.Append("using App.Area").Append(index).Append(";\n");
        }

        return text.Append("\nnamespace App;\n\npublic static class Imports\n{\n}\n").ToString();
    }

    /// <summary>One type long enough to exceed both the file and the type comparison point.</summary>
    public static string LargeType(int members)
    {
        var text = new StringBuilder("namespace App;\n\npublic static class Big\n{\n");
        for (var index = 0; index < members; index++)
        {
            text.Append("    internal static int Value").Append(index).Append(" => ").Append(index).Append(";\n");
        }

        return text.Append("}\n").ToString();
    }

    private static string Statements(int count)
    {
        var text = new StringBuilder();
        for (var index = 0; index < count; index++)
        {
            text.Append("        total += ").Append(index).Append(";\n");
        }

        return text.ToString();
    }
}

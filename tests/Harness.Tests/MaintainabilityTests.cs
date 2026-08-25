namespace Harness.Tests;

public sealed class MaintainabilityTests
{
    private const string Check = "maintainability.csharp";

    [Fact]
    public void A_repository_without_csharp_sources_is_not_applicable()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
        Assert.False(run.OutputContains("passed"), run.Output);
    }

    [Fact]
    public void A_long_method_is_reported_with_its_metric_value_comparison_point_and_location()
    {
        using var repository = SourceRepository("src/App/Report.cs", MaintainabilitySources.LongMethod(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(Check), run.Output);
        Assert.True(run.OutputContains("method logical lines 75 exceeds"), run.Output);
        Assert.True(run.OutputContains("comparison point of 60"), run.Output);
        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.True(run.OutputContains("src/App/Report.cs:5"), run.Output);
    }

    [Fact]
    public void A_tuple_returning_method_is_named_after_the_member_and_not_its_modifier()
    {
        using var repository = SourceRepository("src/App/Report.cs", MaintainabilitySources.LongTupleReturningMethod(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.False(run.OutputContains("App.Report.static"), run.Output);
        Assert.False(run.OutputContains("constructor parameter count"), run.Output);
    }

    [Fact]
    public void Maintainability_findings_are_advisory_and_do_not_fail_the_run()
    {
        using var repository = SourceRepository(
            "src/App/Report.cs",
            MaintainabilitySources.LongMethod(70),
            Frame.AllPresent().Policy(Check, "advisory"));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("advisory"), run.Output);
    }

    [Fact]
    public void A_multiline_signature_is_attributed_to_the_line_the_declaration_starts_on()
    {
        using var repository = SourceRepository("src/App/Report.cs", MaintainabilitySources.LongMethodWithSplitSignature(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("src/App/Report.cs:5"), run.Output);
    }

    [Fact]
    public void Branch_tokens_inside_comments_strings_and_char_literals_are_not_control_flow()
    {
        using var repository = SourceRepository("src/App/Quiet.cs", MaintainabilitySources.BranchTokensOnlyInCommentsAndStrings);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/App/Quiet.cs"), run.Output);
    }

    [Fact]
    public void Real_control_flow_is_reported_as_a_lexical_branch_count()
    {
        using var repository = SourceRepository("src/App/Router.cs", MaintainabilitySources.BranchingMethod);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("lexical branch count"), run.Output);
        Assert.True(run.OutputContains("App.Router.Route"), run.Output);
    }

    [Fact]
    public void A_positional_record_does_not_report_a_constructor_parameter_count()
    {
        using var repository = SourceRepository("src/App/Money.cs", MaintainabilitySources.WideRecord);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("constructor parameter count"), run.Output);
    }

    [Fact]
    public void A_class_primary_constructor_reports_its_parameter_count()
    {
        using var repository = SourceRepository("src/App/Engine.cs", MaintainabilitySources.WidePrimaryConstructor);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("constructor parameter count 7 exceeds"), run.Output);
        Assert.True(run.OutputContains("App.Engine"), run.Output);
    }

    [Fact]
    public void A_declared_constructor_reports_its_parameter_count()
    {
        using var repository = SourceRepository("src/App/Service.cs", MaintainabilitySources.WideConstructor);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("constructor parameter count"), run.Output);
        Assert.True(run.OutputContains("App.Service"), run.Output);
    }

    [Fact]
    public void A_wide_public_surface_is_reported_for_the_declaring_type()
    {
        using var repository = SourceRepository("src/App/Facade.cs", MaintainabilitySources.WidePublicSurface(30));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("public declared members 30 exceeds"), run.Output);
        Assert.True(run.OutputContains("App.Facade"), run.Output);
    }

    [Fact]
    public void A_large_file_and_a_large_type_are_reported_separately()
    {
        using var repository = SourceRepository("src/App/Big.cs", MaintainabilitySources.LargeType(420));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("file logical lines"), run.Output);
        Assert.True(run.OutputContains("type logical lines"), run.Output);
        Assert.True(run.OutputContains("App.Big"), run.Output);
    }

    [Fact]
    public void A_nested_type_is_named_through_its_enclosing_type()
    {
        using var repository = SourceRepository("src/App/Outer.cs", MaintainabilitySources.NestedTypeWithLongMethod(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Outer.Inner.Compute"), run.Output);
    }

    [Fact]
    public void Expression_bodied_members_and_records_do_not_confuse_the_reader()
    {
        using var repository = SourceRepository("src/App/Compact.cs", MaintainabilitySources.ExpressionBodiedMembers);

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/App/Compact.cs"), run.Output);
    }

    [Fact]
    public void A_literal_with_awkward_interpolation_does_not_hide_what_follows_it()
    {
        using var repository = SourceRepository("src/App/Report.cs", MaintainabilitySources.LongMethodAfterAwkwardLiterals(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("App.Report.Compute"), run.Output);
        Assert.False(run.OutputContains("App.Report.Interpolate"), run.Output);
    }

    [Fact]
    public void Generated_and_build_output_sources_are_excluded()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/App/obj/Debug/Report.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Report.g.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Report.generated.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Report.Designer.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Marked.cs", "// <auto-generated />\n" + MaintainabilitySources.LongMethod(70))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Untracked_sources_are_ignored()
    {
        using var repository = Fixtures.Compliant().WriteFile("src/App/Report.cs", MaintainabilitySources.LongMethod(70));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Many_findings_stay_bounded_and_still_report_how_many_there_are()
    {
        var repository = Fixtures.Compliant();
        for (var index = 0; index < 40; index++)
        {
            repository.WriteFile($"src/App/Report{index:00}.cs", MaintainabilitySources.LongMethod(70));
        }

        using var committed = repository.Commit();

        var run = HarnessCli.RunVerbose(committed.Path, "check", "--only", Check);

        var lines = run.Output.Split('\n', StringSplitOptions.RemoveEmptyEntries);
        Assert.True(lines.Length <= 20, run.Output);
        Assert.True(run.OutputContains("40"), run.Output);
    }

    [Fact]
    public void The_gate_reports_a_duration_and_leaves_the_repository_unchanged()
    {
        using var repository = SourceRepository("src/App/Report.cs", MaintainabilitySources.LongMethod(70));
        var before = repository.TrackedState();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.True(run.OutputContains(" ms)"), run.Output);
        Assert.Equal(before, repository.TrackedState());
    }

    [Fact]
    public void Repository_settings_change_maintainability_comparison_points()
    {
        using var repository = SourceRepository(
            "src/App/Report.cs",
            MaintainabilitySources.LongMethod(70),
            Frame.Answering().Settings(
                """{ "maintainability.csharp": { "methodLines": 100 } }"""));

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.False(run.OutputContains("method logical lines"), run.Output);
    }

    [Fact]
    public void Every_documented_metric_is_reachable_from_one_repository()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Policy(Check, "advisory"))
            .WriteFile("src/App/Big.cs", MaintainabilitySources.LargeType(420))
            .WriteFile("src/App/Report.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Router.cs", MaintainabilitySources.BranchingMethod)
            .WriteFile("src/App/Service.cs", MaintainabilitySources.WideConstructor)
            .WriteFile("src/App/Facade.cs", MaintainabilitySources.WidePublicSurface(30))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Check);

        Assert.Equal(0, run.ExitCode);
        foreach (var metric in new[]
        {
            "file logical lines",
            "type logical lines",
            "method logical lines",
            "lexical branch count",
            "constructor parameter count",
            "public declared members",
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

        Assert.True(run.OutputContains("not a dependency count"), run.Output);
        Assert.True(run.OutputContains("not a compiler control-flow graph"), run.Output);
    }

    private static RepositoryFixture SourceRepository(string path, string source, Frame? frame = null)
        => Fixtures.Compliant(frame ?? Frame.AllPresent()).WriteFile(path, source).Commit();
}

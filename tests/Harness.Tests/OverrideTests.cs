namespace Harness.Tests;

/// <summary>
/// A path-scoped override changes the norm a zone is measured by, or excludes the zone from
/// one check entirely; every entry justifies itself or invalidates the whole frame.
/// </summary>
public sealed class OverrideTests
{
    private const string Maintainability = "maintainability.csharp";

    private const string Duplication = "duplication.csharp";

    [Fact]
    public void A_zone_lives_by_its_overridden_numbers_while_the_rest_keeps_the_global_ones()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "maintainability.csharp", "paths": ["src/**/Kubernetes/**"],
                  "reason": "watch/reconnect logic runs long by design",
                  "settings": { "methodLines": 120, "branches": 25 } }
                """))
            .WriteFile("src/App/Kubernetes/Watcher.cs", MaintainabilitySources.LongMethod(70))
            .WriteFile("src/App/Report.cs", MaintainabilitySources.LongMethod(70))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Maintainability);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("Kubernetes/Watcher.cs"), run.Output);
        Assert.True(run.OutputContains("src/App/Report.cs"), run.Output);
    }

    [Fact]
    public void A_finding_inside_a_zone_is_reported_against_the_effective_comparison_point()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "maintainability.csharp", "paths": ["src/**/Kubernetes/**"],
                  "reason": "a softer budget, still a budget",
                  "settings": { "methodLines": 70 } }
                """))
            .WriteFile("src/App/Kubernetes/Watcher.cs", MaintainabilitySources.LongMethod(70))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Maintainability);

        Assert.True(run.OutputContains("method logical lines 75 exceeds"), run.Output);
        Assert.True(run.OutputContains("comparison point of 70"), run.Output);
    }

    [Fact]
    public void An_off_zone_is_excluded_from_the_check_entirely()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "maintainability.csharp", "paths": ["src/Domain/**"],
                  "reason": "the domain layer is excluded from the size budget",
                  "off": true }
                """))
            .WriteFile("src/Domain/Aggregate.cs", MaintainabilitySources.LongMethod(70))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Maintainability);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/Domain/Aggregate.cs"), run.Output);
    }

    [Fact]
    public void Duplication_ignores_files_an_off_zone_excludes_from_the_comparison()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "duplication.csharp", "paths": ["tests/**"],
                  "reason": "test fixtures legalize repetition",
                  "off": true }
                """))
            .WriteFile("src/App/First.cs", DuplicationSources.Block("First", "seed", "alpha"))
            .WriteFile("tests/Fixture/Second.cs", DuplicationSources.Block("Second", "start", "beta"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Duplication);

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("lexically repeated"), run.Output);
    }

    [Fact]
    public void A_check_whose_every_file_is_excluded_says_so_instead_of_passing()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "duplication.csharp", "paths": ["**/*.cs"],
                  "reason": "nothing here is comparable",
                  "off": true }
                """))
            .WriteFile("src/App/First.cs", DuplicationSources.Block("First", "seed", "alpha"))
            .WriteFile("src/App/Second.cs", DuplicationSources.Block("Second", "start", "beta"))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Duplication);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("excluded for this check by 'overrides'"), run.Output);
    }

    [Fact]
    public void A_segment_glob_scopes_an_override_to_matching_files_anywhere()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "maintainability.csharp", "paths": ["**/*Controller.cs"],
                  "reason": "REST controllers aggregate a wide surface",
                  "settings": { "publicMembers": 35 } }
                """))
            .WriteFile("src/Api/UserController.cs", MaintainabilitySources.WidePublicSurface(30))
            .WriteFile("src/App/Facade.cs", MaintainabilitySources.WidePublicSurface(30))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Maintainability);

        Assert.False(run.OutputContains("UserController.cs"), run.Output);
        Assert.True(run.OutputContains("src/App/Facade.cs"), run.Output);
    }

    [Fact]
    public void A_blocking_check_accepts_an_off_zone()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
                """
                { "check": "comments.csharp", "paths": ["src/Legacy/**"],
                  "reason": "historic commentary kept until the rewrite",
                  "settings": { "percentageLimit": 90 } }
                """).Overriding(
                """
                { "check": "types-per-file.csharp", "paths": ["src/Legacy/**"],
                  "reason": "historic files bundle types until the rewrite",
                  "off": true }
                """))
            .WriteFile("src/Legacy/Noisy.cs", CommentHeavySource)
            .WriteFile("src/Legacy/Pair.cs", TwoTypesSource)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("src/Legacy/Noisy.cs"), run.Output);
        Assert.False(run.OutputContains("src/Legacy/Pair.cs"), run.Output);
    }

    [Fact]
    public void An_override_without_a_reason_invalidates_the_frame()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
            """{ "check": "maintainability.csharp", "paths": ["src/**"], "settings": { "methodLines": 120 } }"""));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("overrides[0].reason"), run.Output);
    }

    [Fact]
    public void An_override_carries_exactly_one_of_settings_or_off()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
            """
            { "check": "maintainability.csharp", "paths": ["src/**"], "reason": "both at once",
              "settings": { "methodLines": 120 }, "off": true }
            """));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("exactly one of 'settings' or 'off'"), run.Output);
    }

    [Fact]
    public void An_override_can_only_name_a_path_scoped_check()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
            """{ "check": "dependencies.csharp", "paths": ["src/**"], "reason": "edges", "off": true }"""));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("path-scoped check"), run.Output);
    }

    [Fact]
    public void An_override_setting_is_validated_against_the_check_that_reads_it()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
            """
            { "check": "maintainability.csharp", "paths": ["src/**"], "reason": "typo",
              "settings": { "methodLenght": 120 } }
            """));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("overrides[0].settings.methodLenght"), run.Output);
    }

    [Fact]
    public void A_check_without_settings_accepts_only_off()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Overriding(
            """
            { "check": "duplication.csharp", "paths": ["src/**"], "reason": "no numbers to move",
              "settings": { "windowLines": 12 } }
            """));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("has no settings"), run.Output);
    }

    [Fact]
    public void A_pin_older_than_the_section_refuses_overrides_and_names_the_way_forward()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("1.0.0").Overriding(
            """{ "check": "maintainability.csharp", "paths": ["src/**"], "reason": "zone", "off": true }"""));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("harness upgrade"), run.Output);
    }

    private const string CommentHeavySource =
        """
        namespace App;

        // historical note 1
        // historical note 2
        // historical note 3
        // historical note 4
        // historical note 5
        // historical note 6
        // historical note 7
        // historical note 8
        // historical note 9
        // historical note 10
        public static class Noisy
        {
            public static int Value() => 1;
        }

        """;

    private const string TwoTypesSource =
        """
        namespace App;

        public sealed class First
        {
            public int Value { get; init; }
        }

        public sealed class Second
        {
            public int Value { get; init; }
        }

        """;
}

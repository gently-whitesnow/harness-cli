using System.Text.Json.Nodes;

namespace Harness.Tests;

public sealed class WorkspaceTests
{
    [Theory]
    [InlineData("off")]
    [InlineData("advisory")]
    public void Project_document_policy_does_not_weaken_root_or_siblings(string policy)
    {
        using var repository = Workspace("profile", "app")
            .WriteFile("profile/.harness.json", ProjectFrame(policy))
            .WriteFile("profile/SOUL.md", "# Profile data\n")
            .Commit();

        var accepted = HarnessCli.RunVerbose(repository.Path, "check");
        Assert.Equal(0, accepted.ExitCode);
        Assert.Contains("profile", accepted.Output, StringComparison.Ordinal);

        repository.WriteFile("app/notes.md", "# Invalid sibling document\n").Commit();
        var sibling = HarnessCli.RunVerbose(repository.Path, "check");
        Assert.Equal(1, sibling.ExitCode);
        Assert.Contains("notes.md", sibling.Output, StringComparison.Ordinal);

        repository.Remove("app/notes.md").WriteFile("outside/notes.md", "# Root-owned document\n").Commit();
        var root = HarnessCli.RunVerbose(repository.Path, "check");
        Assert.Equal(1, root.ExitCode);
        Assert.Contains("outside/notes.md", root.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Running_inside_a_project_still_checks_the_entire_workspace()
    {
        using var repository = Workspace("one", "two")
            .WriteFile("two/notes.md", "# Invalid\n").Commit();

        var root = HarnessCli.Run(repository.Path, "check");
        var nested = HarnessCli.Run(repository.Absolute("one"), "check");

        Assert.Equal(1, root.ExitCode);
        Assert.Equal(root.Output, nested.Output);
    }

    [Fact]
    public void Project_selection_is_explicitly_partial_and_excludes_sibling_findings()
    {
        using var repository = Workspace("one", "two")
            .WriteFile("two/notes.md", "# Invalid\n").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "one");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("partial", run.Output, StringComparison.OrdinalIgnoreCase);
        Assert.DoesNotContain("notes.md", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void A_selected_project_does_not_hide_a_malformed_sibling_frame()
    {
        using var repository = Workspace("one", "two")
            .WriteFile("two/.harness.json", "{ invalid json").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "one");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("two/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Incomplete_project_takes_precedence_over_a_proven_failure()
    {
        using var repository = Workspace("one", "two")
            .WriteFile("one/notes.md", "# Invalid\n")
            .WriteFile("two/.harness.json", """{"settings":{},"policy":{"frame.verify":"required"},"answers":{"verify":{}}}""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
    }

    [Theory]
    [InlineData("[\"app\",\"app\"]")]
    [InlineData("[\"app\",\"app/nested\"]")]
    [InlineData("[\"../app\"]")]
    [InlineData("[\"/app\"]")]
    [InlineData("[\"./app\"]")]
    [InlineData("[\"app/\"]")]
    [InlineData("[\"app/../other\"]")]
    [InlineData("[\".\"]")]
    [InlineData("[\"\"]")]
    [InlineData("[42]")]
    [InlineData("\"app\"")]
    public void Invalid_project_registration_is_incomplete(string projects)
    {
        using var repository = Workspace("app");
        var root = JsonNode.Parse(RootFrame("app"))!.AsObject();
        root["projects"] = JsonNode.Parse(projects);
        repository.WriteFile(".harness.json", root.ToJsonString()).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("projects", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("version", "\"latest\"")]
    [InlineData("projects", "[]")]
    [InlineData("settings", "{\"commits\":{\"language\":\"en\",\"requireSetup\":false}}")]
    [InlineData("policy", "{\"commits.setup\":\"off\"}")]
    public void Repository_owned_settings_are_rejected_in_projects(string key, string value)
    {
        using var repository = Workspace("app");
        var child = JsonNode.Parse(ProjectFrame())!.AsObject();
        child[key] = JsonNode.Parse(value);
        repository.WriteFile("app/.harness.json", child.ToJsonString()).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("app/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Registered_project_requires_its_config_in_the_index()
    {
        using var repository = Workspace("app");
        repository.Git("rm", "--cached", "app/.harness.json");

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("app/.harness.json", run.Output, StringComparison.Ordinal);
        Assert.Contains("not in the index", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Registered_project_with_missing_config_is_incomplete()
    {
        using var repository = Workspace("app").Remove("app/.harness.json").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("app/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unregistered_tracked_config_is_incomplete()
    {
        using var repository = Workspace("app")
            .WriteFile("other/.harness.json", ProjectFrame()).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("other/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Deleted_tracked_project_frame_falls_back_to_the_index()
    {
        using var repository = Workspace("app");
        repository.Remove("app/.harness.json");

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Parent_applicability_is_not_inherited()
    {
        using var repository = Workspace("app");
        var root = JsonNode.Parse(RootFrame("app"))!.AsObject();
        root["answers"] = JsonNode.Parse("""{"verify":{"paths":["verify.sh"]}}""");
        root["applicability"] = JsonNode.Parse("""{"yaml":{"applicable":true}}""");
        repository.WriteFile(".harness.json", root.ToJsonString())
            .WriteFile("app/.harness.json", """{"policy":{"comments.yaml":"required"},"settings":{"comments.yaml":{"minimumCommentLines":10,"percentageLimit":8}}}""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("applicability.yaml", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("missing")]
    [InlineData("app/nested")]
    public void Project_selector_must_name_a_registered_project(string project)
    {
        using var repository = Workspace("app");

        var run = HarnessCli.Run(repository.Path, "check", "--project", project);

        Assert.Equal(2, run.ExitCode);
        Assert.Contains(project, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_selector_requires_a_value()
    {
        using var repository = Workspace("app");

        var run = HarnessCli.Run(repository.Path, "check", "--project");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--project", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_can_read_shared_ancestor_build_properties()
    {
        using var repository = Workspace("apps/service")
            .WriteFile("Directory.Build.props", Fixtures.HardenedBuildProps)
            .WriteFile("apps/service/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("apps/service/.harness.json", """{"settings":{},"applicability":{"dotnet":{"applicable":true}},"policy":{"build-properties.dotnet":"required"}}""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/service");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("build-properties.dotnet", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_can_read_shared_ancestor_editorconfig()
    {
        using var repository = Workspace("apps/service")
            .WriteFile("apps/service/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("apps/service/.harness.json", """{"settings":{},"applicability":{"dotnet":{"applicable":true}},"policy":{"editorconfig.dotnet":"required"}}""");
        var explained = HarnessCli.Run(repository.Path, "explain", "editorconfig.dotnet");
        Assert.Equal(0, explained.ExitCode);
        repository.WriteFile(".editorconfig", EditorConfigTests.ReferenceFileFrom(explained.StandardOutput)).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "apps/service");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("every project reads the shared code-style baseline", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_contract_pin_applies_to_selected_project()
    {
        using var repository = Workspace("app");
        var root = JsonNode.Parse(RootFrame("app"))!.AsObject();
        root["version"] = "0.0.1";
        repository.WriteFile(".harness.json", root.ToJsonString()).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "app");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("0.0.1", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unstaged_malformed_project_frame_is_incomplete()
    {
        using var repository = Workspace("app");
        repository.WriteFile("app/.harness.json", "{ invalid unstaged content");

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("app/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_frame_accepts_comments_and_trailing_commas_like_root_frames()
    {
        using var repository = Workspace("app")
            .WriteFile("app/.harness.json", """
                {
                  // This component owns its policy.
                  "settings": {},
                  "policy": { "docs.policy": "required", },
                }
                """).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Root_answer_failure_cannot_be_hidden_by_a_check_selector()
    {
        using var repository = Workspace("app");
        var root = JsonNode.Parse(RootFrame("app"))!.AsObject();
        root["policy"]!["frame.verify"] = "required";
        root["answers"] = JsonNode.Parse("""{"verify":{}}""");
        repository.WriteFile(".harness.json", root.ToJsonString()).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("verify", run.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData("other/*.cs", 0)]
    [InlineData("app/*.cs", 1)]
    public void Shared_editorconfig_suppression_is_judged_only_for_its_owning_project(string pattern, int expected)
    {
        using var repository = Workspace("app")
            .WriteFile(".editorconfig", $"root = true\n[{pattern}]\ndotnet_diagnostic.CA1707.severity = none\n")
            .WriteFile("app/App.csproj", Fixtures.SimpleSdkProject)
            .WriteFile("app/Widget.cs", Fixtures.FormattedSource)
            .WriteFile("app/.harness.json", """{"settings":{},"applicability":{"dotnet":{"applicable":true}},"policy":{"warning-suppressions.dotnet":"required"}}""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--project", "app");

        Assert.Equal(expected, run.ExitCode);
    }

    [Fact]
    public void Upgrade_suggestions_use_project_sources_without_expanding_root_frame()
    {
        using var repository = Workspace("app")
            .WriteFile("app/main.go", "package main\nfunc main() {}\n").Commit();
        var before = repository.TrackedState();

        var run = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        var projectHeading = run.Output.IndexOf("Project app (app/.harness.json)", StringComparison.Ordinal);
        Assert.True(projectHeading >= 0, run.Output);
        Assert.DoesNotContain("comments.go", run.Output[..projectHeading], StringComparison.Ordinal);
        var project = run.Output[projectHeading..];
        Assert.Contains("comments.go", project, StringComparison.Ordinal);
        Assert.DoesNotContain("commits.setup", project, StringComparison.Ordinal);
        Assert.DoesNotContain("\"commits\"", project, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\"", project, StringComparison.Ordinal);
        Assert.Equal(before, repository.TrackedState());
    }

    [Theory]
    [InlineData("{\"settings\":{},\"policy\":{\"docs.policy\":\"required\",\"docs.policy\":\"off\"}}")]
    [InlineData("{\"settings\":{},\"settings\":{},\"policy\":{}}")]
    public void Duplicate_properties_in_project_frames_are_incomplete(string frame)
    {
        using var repository = Workspace("app").WriteFile("app/.harness.json", frame).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("app/.harness.json", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Parent_answers_do_not_claim_readiness_for_a_project()
    {
        using var repository = Workspace("app");
        var root = JsonNode.Parse(RootFrame("app"))!.AsObject();
        root["answers"] = JsonNode.Parse("""{"verify":{"paths":["verify.sh"]}}""");
        repository.WriteFile(".harness.json", root.ToJsonString())
            .WriteFile("app/.harness.json", """{"settings":{},"policy":{"frame.verify":"required"}}""")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("answers", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Unresolved_merge_conflict_is_reported_not_crashed()
    {
        using var repository = Workspace("app").WriteFile("app/shared.txt", "base\n").CommitAs("base");
        repository.Git("checkout", "--quiet", "-b", "feature");
        repository.WriteFile("app/shared.txt", "feature\n").CommitAs("feature");
        repository.Git("checkout", "--quiet", "main");
        repository.WriteFile("app/shared.txt", "main\n").CommitAs("main");
        var merge = ProcessLauncher.Run("git", ["merge", "feature"], repository.Path);
        Assert.NotEqual(0, merge.ExitCode);
        Assert.Contains("app/shared.txt", repository.Git("diff", "--name-only", "--diff-filter=U"), StringComparison.Ordinal);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.True(run.ExitCode is 0 or 1 or 2, run.Output);
        Assert.DoesNotContain("Unhandled exception", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("same key", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Duplicate_properties_in_the_root_frame_are_incomplete_and_named_by_upgrade()
    {
        using var repository = Fixtures.WithRawFrame(
            """{"version":"latest","settings":{"commits":{"language":"ru","requireSetup":false}},"policy":{"docs.policy":"required","docs.policy":"off"}}""");

        var check = HarnessCli.RunVerbose(repository.Path, "check");
        var upgrade = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(2, check.ExitCode);
        Assert.Contains("duplicate property 'policy.docs.policy'", check.Output, StringComparison.Ordinal);
        Assert.Equal(2, upgrade.ExitCode);
        Assert.Contains("duplicate property 'policy.docs.policy'", upgrade.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Upgrade_names_unregistered_nested_configs()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("docs/examples/.harness.json", ProjectFrame()).Commit();

        var upgrade = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");
        var check = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, upgrade.ExitCode);
        Assert.Contains("docs/examples/.harness.json", upgrade.Output, StringComparison.Ordinal);
        Assert.Equal(2, check.ExitCode);
        Assert.Contains("harness.config", check.Output, StringComparison.Ordinal);
        Assert.Contains("harness " + Release.Current, check.Output, StringComparison.Ordinal);
        Assert.Contains("docs/examples/.harness.json", check.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("WORKSPACE RUN", check.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Missing_project_config_shows_the_project_template()
    {
        using var repository = Workspace("app").Remove("app/.harness.json").Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("for a registered project", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("\"version\"", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("not in the index", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_selector_is_reported_when_the_root_frame_cannot_be_read()
    {
        using var repository = Workspace("app").WriteFile(".harness.json", "{ invalid").Commit();

        var run = HarnessCli.Run(repository.Path, "check", "--project", "app");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Project 'app' was not verified", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Root_only_selection_says_the_selected_project_was_not_run()
    {
        using var repository = Workspace("app");

        var run = HarnessCli.Run(repository.Path, "check", "--project", "app", "--only", "commits");

        Assert.Contains("Project 'app' was not run", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("other projects were not measured", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Scope: app", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Project_selector_on_a_plain_repository_names_the_missing_registration()
    {
        using var repository = Fixtures.Compliant();

        var run = HarnessCli.Run(repository.Path, "check", "--project", "src");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("registers no projects", run.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("other projects were not measured", run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Workspace_headline_agrees_with_the_scope_verdicts()
    {
        using var repository = Workspace("app")
            .WriteFile("app/.harness.json", ProjectFrame("advisory"))
            .WriteFile("app/notes.md", "# Advisory finding\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.StartsWith("PASS  WORKSPACE RUN", run.Output, StringComparison.Ordinal);
        Assert.Contains("POLICY    TIME", run.Output, StringComparison.Ordinal);
        Assert.Contains("advisory  ", run.Output, StringComparison.Ordinal);
    }

    private static RepositoryFixture Workspace(params string[] projects)
    {
        var repository = Fixtures.WithRawFrame(RootFrame(projects));
        foreach (var project in projects)
        {
            repository.WriteFile($"{project}/.harness.json", ProjectFrame())
                .WriteFile($"{project}/AGENTS.md", "# Project\n")
                .WriteSymbolicLink($"{project}/CLAUDE.md", "AGENTS.md");
        }

        return repository.Commit();
    }

    private static string RootFrame(params string[] projects)
    {
        var frame = JsonNode.Parse("""{"version":"latest","settings":{"commits":{"language":"ru","requireSetup":false}},"policy":{"docs.policy":"required"}}""")!.AsObject();
        frame["projects"] = new JsonArray(projects.Select(project => (JsonNode?)JsonValue.Create(project)).ToArray());
        return frame.ToJsonString();
    }

    private static string ProjectFrame(string policy = "required")
        => $$$"""{"settings":{},"policy":{"docs.policy":"{{{policy}}}"}}""";
}

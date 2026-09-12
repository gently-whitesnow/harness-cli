using System.Text.Json;

namespace Harness.Tests;

/// <summary>
/// Contract 3.0 (ADR-0054): a check the policy does not name is outside the frame, a check it
/// names is complete, init writes only the detected axes, and harness.coverage names the rest.
/// </summary>
public sealed class ExplicitFrameTests
{
    private const string MinimalFrame =
        """
        {
          "version": "latest",
          "settings": { "commits": { "language": "ru", "requireSetup": false } },
          "policy": { "docs.policy": "required" }
        }
        """;

    [Fact]
    public void A_frame_naming_one_check_runs_only_that_check()
    {
        using var repository = Fixtures.WithRawFrame(MinimalFrame);

        var run = HarnessCli.Run(repository.Path, "check");
        var verbose = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.Output.StartsWith("PASS", StringComparison.Ordinal), run.Output);
        Assert.True(run.OutputContains("docs.policy"), run.Output);
        Assert.False(run.OutputContains("comments.csharp"), run.Output);
        Assert.False(run.OutputContains("frame.verify"), run.Output);
        Assert.True(run.OutputContains("outside the frame"), run.Output);
        Assert.True(verbose.OutputContains("outside the frame  36 checks not named in policy: harness.coverage"), verbose.Output);
    }

    [Fact]
    public void A_check_outside_the_frame_does_not_run_even_when_it_would_fail()
    {
        using var repository = Fixtures.WithRawFrame(MinimalFrame.Replace("docs.policy", "commits.setup", StringComparison.Ordinal))
            .WriteLines("AGENTS.md", 400)
            .Commit();

        var run = HarnessCli.Run(repository.Path, "check");
        var named = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("docs.policy"), run.Output);
        Assert.Equal(0, named.ExitCode);
        Assert.True(named.OutputContains("docs.policy"), named.Output);
        Assert.True(named.OutputContains("outside the frame"), named.Output);
        Assert.True(named.OutputContains("outcome: skipped"), named.Output);
    }

    [Fact]
    public void A_policy_entry_without_its_applicability_axis_is_incomplete()
    {
        var frame = Frame.AllPresent().ToString().Replace(
            ",\n    \"dotnet\": { \"applicable\": true }",
            string.Empty,
            StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("'policy.build-properties.dotnet' is declared, but 'applicability.dotnet' is not"), run.Output);
    }

    [Fact]
    public void A_settings_section_for_a_check_outside_the_policy_is_incomplete()
    {
        var frame = Frame.AllPresent().ToString().Replace(
            "    \"comments.yaml\": \"required\",\n",
            string.Empty,
            StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("'settings.comments.yaml' is declared, but 'policy' does not mention comments.yaml"), run.Output);
    }

    [Fact]
    public void A_named_check_without_its_settings_section_is_incomplete()
    {
        var frame = Frame.AllPresent().ToString().Replace(
            "\"comments.yaml\": { \"minimumCommentLines\": 10, \"percentageLimit\": 8 }, ",
            string.Empty,
            StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame);

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("'settings' is missing explicit sections for checks in 'policy': comments.yaml"), run.Output);
    }

    [Fact]
    public void Architecture_is_required_once_an_architecture_check_is_named()
    {
        using var repository = Fixtures.WithRawFrame(
            MinimalFrame.Replace("docs.policy", "architecture.sliced-dotnet.zone-shape", StringComparison.Ordinal)
                .Replace("\"settings\"", "\"applicability\": { \"csharp\": { \"applicable\": true } },\n  \"settings\"", StringComparison.Ordinal));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("'policy' names architecture.sliced-dotnet checks, so 'architecture' must select"), run.Output);
    }

    [Fact]
    public void An_unanswered_question_outside_the_policy_is_not_a_failure()
    {
        using var repository = Fixtures.WithRawFrame(
            MinimalFrame.Replace("docs.policy", "frame.verify", StringComparison.Ordinal)
                .Replace("\"settings\"", "\"answers\": { \"verify\": { \"paths\": [\"verify.sh\"] } },\n  \"settings\"", StringComparison.Ordinal));

        var run = HarnessCli.RunVerbose(repository.Path, "check");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("frame.verify"), run.Output);
        Assert.False(run.OutputContains("answers.tests.unit"), run.Output);
    }

    [Fact]
    public void Coverage_reports_a_tracked_language_the_frame_does_not_declare()
    {
        using var repository = Fixtures.WithRawFrame(MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal))
            .WriteFile("deploy/values.yml", "key: value\n")
            .WriteFile("node_modules/lib/index.js", "module.exports = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("1 tracked YAML source (deploy/values.yml) but no `applicability.yaml` entry"), run.Output);
        Assert.True(run.OutputContains("\"yaml\": { \"applicable\": true }"), run.Output);
        Assert.True(run.OutputContains("\"comments.yaml\": {"), run.Output);
        Assert.True(run.OutputContains("\"minimumCommentLines\": 10"), run.Output);
        Assert.True(run.OutputContains("\"comments.yaml\": \"required\""), run.Output);
        Assert.False(run.OutputContains("typescript"), run.Output);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Adding_csharp_offers_both_architecture_kinds_and_every_init_policy(bool declared)
    {
        var frame = MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal);
        if (declared)
        {
            frame = frame.Replace("\"settings\"", "\"applicability\": { \"csharp\": { \"applicable\": true } }, \"settings\"", StringComparison.Ordinal);
        }

        using var repository = Fixtures.WithRawFrame(frame).WriteFile("App.cs", "public sealed class App {}\n").Commit();
        using var initialized = RepositoryFixture.CreateGitRepository().WriteFile("App.cs", "public sealed class App {}\n").Commit();
        Assert.Equal(0, HarnessCli.Run(initialized.Path, "init", "--kind", "application").ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(initialized.Absolute(".harness.json")));
        var architectureIds = document.RootElement.GetProperty("policy").EnumerateObject()
            .Where(entry => entry.Name.StartsWith("architecture.", StringComparison.Ordinal)).Select(entry => entry.Name).ToList();
        Assert.Equal(8, architectureIds.Count);

        var upgrade = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");
        Assert.Equal(0, upgrade.ExitCode);
        var outputs = new List<string> { upgrade.Output };
        if (!declared)
        {
            var coverage = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");
            Assert.Equal(1, coverage.ExitCode);
            outputs.Add(coverage.Output);
        }

        foreach (var output in outputs)
        {
            Assert.Contains("application: \"architecture\": { \"standard\": \"sliced-dotnet/1\" }", output, StringComparison.Ordinal);
            Assert.Contains("library: \"architecture\": { \"applicable\": false", output, StringComparison.Ordinal);
            foreach (var id in architectureIds)
            {
                Assert.Contains($"\"{id}\": \"required\"", output, StringComparison.Ordinal);
            }
        }

        Assert.Equal(frame, File.ReadAllText(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Adding_csharp_keeps_an_existing_architecture_decision()
    {
        var frame = MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal)
            .Replace("\"settings\"", "\"architecture\": { \"applicable\": false, \"reason\": \"library\" }, \"settings\"", StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame).WriteFile("App.cs", "public sealed class App {}\n").Commit();

        var coverage = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");
        var upgrade = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(1, coverage.ExitCode);
        Assert.Equal(0, upgrade.ExitCode);
        Assert.DoesNotContain("Choose the repository kind", coverage.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("Choose the repository kind", upgrade.Output, StringComparison.Ordinal);
        Assert.Equal(frame, File.ReadAllText(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Coverage_accepts_a_declined_axis_and_a_declared_one()
    {
        using var repository = Fixtures.WithRawFrame(
            MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal)
                .Replace(
                    "\"settings\"",
                    "\"applicability\": { \"yaml\": { \"applicable\": false, \"reason\": \"generated deploy manifests\" } },\n  \"settings\"",
                    StringComparison.Ordinal))
            .WriteFile("deploy/values.yml", "key: value\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("yaml: 1 tracked source, declined — \"generated deploy manifests\""), run.Output);
    }

    [Fact]
    public void Coverage_prints_the_suffixes_no_axis_reads_without_a_finding()
    {
        using var repository = Fixtures.WithRawFrame(
            MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal)
                .Replace("\"settings\"", "\"applicability\": { \"yaml\": { \"applicable\": true } },\n  \"settings\"", StringComparison.Ordinal))
            .WriteFile("deploy/values.yml", "key: value\n")
            .WriteFile("roles/app/templates/compose.yml.j2", "name: app\n")
            .WriteFile("roles/app/templates/unit.j2", "[Unit]\n")
            .WriteFile("tools/render.py", "print(1)\n")
            .WriteFile("inventory.ini", "[all]\n")
            .WriteFile("Makefile", "all:\n")
            .WriteFile("docs/GUIDE.md", "# Guide\n")
            .WriteFile(".gitattributes", "* text=auto\n")
            .WriteFile("vendor/lib/setup.py", "print(2)\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("sources of no axis: .j2 2, .ini 1, .py 1, no suffix 1"), run.Output);
    }

    [Fact]
    public void Coverage_names_untracked_sources_it_cannot_see()
    {
        using var repository = Fixtures.WithRawFrame(MinimalFrame.Replace("docs.policy", "harness.coverage", StringComparison.Ordinal))
            .WriteFile("deploy/values.yml", "key: value\n")
            .Commit()
            .WriteFile("web/app.ts", "export const value = 1;\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("not in the index  web/app.ts"), run.Output);
    }

    [Fact]
    public void Init_declares_only_the_axes_the_index_shows()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("deploy/values.yml", "key: value\n")
            .WriteFile("vendor/lib/App.cs", "sealed class Vendored;")
            .Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.StandardError);
        Assert.DoesNotContain("Repository kind", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Declared yaml from the tracked sources", run.StandardOutput, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".editorconfig")));

        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        var root = document.RootElement;
        Assert.False(root.TryGetProperty("architecture", out _));
        Assert.Equal(["yaml"], root.GetProperty("applicability").EnumerateObject().Select(axis => axis.Name));
        Assert.Equal(["comments.yaml", "commits"], root.GetProperty("settings").EnumerateObject().Select(section => section.Name));
        Assert.Equal(
            ["harness.config", "harness.coverage", "docs.policy", "commits.setup", "comments.yaml"],
            root.GetProperty("policy").EnumerateObject().Select(entry => entry.Name).Where(id => !id.StartsWith("frame.", StringComparison.Ordinal)));

        repository.CommitAs("chore(harness): инициализировать рамку репозитория");
        var check = HarnessCli.Run(repository.Path, "check", "--skip", "frame,docs,commits");
        Assert.Equal(0, check.ExitCode);
        Assert.False(check.OutputContains("not applicable"), check.Output);
    }

    [Fact]
    public void Languages_option_replaces_detection()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("deploy/values.yml", "key: value\n")
            .Commit();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init", "--languages", "csharp,dotnet", "--kind", "library");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Declared csharp, dotnet from the --languages option", run.StandardOutput, StringComparison.Ordinal);
        Assert.True(File.Exists(repository.Absolute(".editorconfig")));
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        var root = document.RootElement;
        Assert.False(root.GetProperty("architecture").GetProperty("applicable").GetBoolean());
        Assert.Equal(["csharp", "dotnet"], root.GetProperty("applicability").EnumerateObject().Select(axis => axis.Name));
        Assert.True(root.GetProperty("policy").TryGetProperty("build-properties.dotnet", out _));
        Assert.False(root.GetProperty("policy").TryGetProperty("comments.yaml", out _));
    }

    [Fact]
    public void Languages_option_rejects_an_unknown_key()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init", "--languages", "rust");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("rust", run.StandardError, StringComparison.Ordinal);
        Assert.Contains("Known keys: csharp, yaml, typescript, go, ansible, dotnet", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Upgrade_prints_only_the_route_after_the_pin_and_the_missing_sections()
    {
        var frame = Frame.AllPresent().Version("2.16.0").ToString()
            .Replace("    \"harness.coverage\": \"required\",\n", string.Empty, StringComparison.Ordinal)
            .Replace(",\n    \"yaml\": { \"applicable\": true }", string.Empty, StringComparison.Ordinal)
            .Replace("\"comments.yaml\": { \"minimumCommentLines\": 10, \"percentageLimit\": 8 }, ", string.Empty, StringComparison.Ordinal)
            .Replace("    \"comments.yaml\": \"required\",\n", string.Empty, StringComparison.Ordinal);
        using var repository = Fixtures.WithRawFrame(frame)
            .WriteFile("deploy/values.yml", "key: value\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "upgrade");

        Assert.Equal(0, run.ExitCode);
        Assert.Contains("Release 2.17 changes", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Contract 3.0 migration", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Contract 2.0 migration", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("Release 2.16 changes", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Sections to add for tracked YAML sources", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"comments.yaml\": \"required\"", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("the policy does not name yet", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("\"harness.coverage\": \"required\"", run.StandardOutput, StringComparison.Ordinal);
        Assert.DoesNotContain("comments.typescript", run.StandardOutput, StringComparison.Ordinal);
        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", "harness.config").ExitCode);
    }
}

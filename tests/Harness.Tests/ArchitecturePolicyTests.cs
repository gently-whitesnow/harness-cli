using System.Text.Json.Nodes;

namespace Harness.Tests;

public sealed class ArchitecturePolicyTests
{
    private const string Family = "architecture.sliced-dotnet";

    [Theory]
    [InlineData("required")]
    [InlineData("advisory")]
    [InlineData("off")]
    public void Upgrade_expands_the_old_policy_without_guessing_answers(string mode)
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("2.16.0"));
        var frame = JsonNode.Parse(File.ReadAllText(repository.Absolute(".harness.json")))!;
        var policy = frame["policy"]!.AsObject();
        var ids = policy.Select(pair => pair.Key).Where(key => key.StartsWith(Family + ".", StringComparison.Ordinal)).ToList();
        foreach (var id in ids)
        {
            policy.Remove(id);
        }
        policy[Family] = mode;
        var before = frame.ToJsonString();
        repository.WriteFile(".harness.json", before);

        Assert.Equal(0, HarnessCli.Run(repository.Path, "upgrade", "--dry-run").ExitCode);
        Assert.Equal(before, File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal(0, HarnessCli.Run(repository.Path, "upgrade").ExitCode);
        var after = JsonNode.Parse(File.ReadAllText(repository.Absolute(".harness.json")))!;
        Assert.Null(after["policy"]![Family]);
        Assert.Equal(8, ids.Count);
        foreach (var id in ids)
        {
            Assert.Equal(mode, after["policy"]![id]!.GetValue<string>());
        }
        Assert.True(JsonNode.DeepEquals(frame["answers"], after["answers"]));
        Assert.True(JsonNode.DeepEquals(frame["settings"], after["settings"]));
        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", Family).ExitCode);
    }

    [Theory]
    [InlineData("architecture")]
    [InlineData(Family)]
    public void Group_selectors_and_individual_skip_preserve_other_rules(string selector)
    {
        using var repository = Application();
        var all = HarnessCli.RunVerbose(repository.Path, "check", "--only", selector);
        Assert.Equal(1, all.ExitCode);
        Assert.Contains("segments-by-purpose", all.Output, StringComparison.Ordinal);
        Assert.Contains("Application public API sidestep", all.Output, StringComparison.Ordinal);

        var skip = HarnessCli.RunVerbose(repository.Path, "check", "--only", selector,
            "--skip", Family + ".segment-names");
        Assert.Equal(1, skip.ExitCode);
        Assert.DoesNotContain("segments-by-purpose", skip.Output, StringComparison.Ordinal);
        Assert.Contains("Application public API sidestep", skip.Output, StringComparison.Ordinal);

        var skipGroup = HarnessCli.Run(repository.Path, "check", "--only", Family, "--skip", selector);
        Assert.Equal(0, skipGroup.ExitCode);
        Assert.Contains("NOTHING VERIFIED", skipGroup.Output, StringComparison.Ordinal);
        Assert.Equal(0, HarnessCli.Run(repository.Path, "explain", selector).ExitCode);
    }

    [Theory]
    [InlineData("required", 1)]
    [InlineData("advisory", 0)]
    [InlineData("off", 0)]
    public void Individual_policy_controls_only_its_own_findings(string mode, int expected)
    {
        using var repository = Application(mode);
        var single = HarnessCli.RunVerbose(repository.Path, "check", "--only", Family + ".segment-names");
        Assert.Equal(expected, single.ExitCode);
        Assert.Equal(1, HarnessCli.Run(repository.Path, "check", "--only", Family).ExitCode);
        if (mode == "advisory")
        {
            Assert.Contains("advisory", single.Output, StringComparison.Ordinal);
            Assert.Contains("segments-by-purpose", single.Output, StringComparison.Ordinal);
        }
        Assert.Equal(0, HarnessCli.Run(repository.Path, "explain", Family + ".segment-names").ExitCode);
    }

    [Fact]
    public void Neutral_architecture_map_is_verbose_detail_only()
    {
        using var repository = Application();
        var concise = HarnessCli.Run(repository.Path, "check", "--only", Family + ".zone-shape");
        var verbose = HarnessCli.RunVerbose(repository.Path, "check", "--only", Family + ".zone-shape");
        Assert.Equal(0, concise.ExitCode);
        Assert.DoesNotContain("architecture map:", concise.Output, StringComparison.Ordinal);
        Assert.Contains("architecture map:", verbose.Output, StringComparison.Ordinal);
        Assert.DoesNotContain("advisory", verbose.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Missing_individual_policy_and_aggregate_policy_are_incomplete(bool aggregate)
    {
        using var repository = Application();
        var frame = JsonNode.Parse(File.ReadAllText(repository.Absolute(".harness.json")))!;
        frame["policy"]!.AsObject().Remove(Family + ".public-api");
        if (aggregate)
        {
            frame["policy"]![Family] = "required";
        }
        repository.WriteFile(".harness.json", frame.ToJsonString());
        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Family);
        Assert.Equal(2, run.ExitCode);
        Assert.Contains(Family, run.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Native_upgrade_migrates_rolling_policy_and_rejects_conflicting_keys_without_writing()
    {
        var (executable, _) = NativePublication.Get();
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("latest"));
        var frame = JsonNode.Parse(File.ReadAllText(repository.Absolute(".harness.json")))!;
        var policy = frame["policy"]!.AsObject();
        policy[Family] = "off";
        var conflicting = frame.ToJsonString();
        repository.WriteFile(".harness.json", conflicting);
        Assert.Equal(2, ProcessLauncher.Run(executable, ["upgrade"], repository.Path).ExitCode);
        Assert.Equal(conflicting, File.ReadAllText(repository.Absolute(".harness.json")));
        foreach (var id in policy.Select(pair => pair.Key)
            .Where(key => key.StartsWith(Family + ".", StringComparison.Ordinal)).ToList())
        {
            policy.Remove(id);
        }
        repository.WriteFile(".harness.json", frame.ToJsonString());
        var migrated = ProcessLauncher.Run(executable, ["upgrade"], repository.Path);
        Assert.True(migrated.ExitCode == 0, migrated.Output);
        var after = JsonNode.Parse(File.ReadAllText(repository.Absolute(".harness.json")))!;
        Assert.Equal("latest", after["version"]!.GetValue<string>());
        Assert.Null(after["policy"]![Family]);
        Assert.Equal("off", after["policy"]![Family + ".cross-api"]!.GetValue<string>());
        Assert.Equal(0, ProcessLauncher.Run(executable, ["check", "--only", Family], repository.Path).ExitCode);
    }

    [Fact]
    public void Malformed_projects_do_not_disable_independent_source_rules()
    {
        using var repository = Application("off")
            .WriteFile("src/Shop/Host/App.csproj", "<Project")
            .Commit();
        Assert.Equal(2, HarnessCli.Run(repository.Path, "check", "--only", Family).ExitCode);
        var selected = HarnessCli.RunVerbose(repository.Path, "check", "--only", Family + ".public-api");
        Assert.Equal(1, selected.ExitCode);
        Assert.Contains("Application public API sidestep", selected.Output, StringComparison.Ordinal);
        Assert.Equal(1, HarnessCli.Run(repository.Path, "check", "--only", Family,
            "--skip", Family + ".layer-assemblies").ExitCode);
    }

    [Theory]
    [InlineData("required", "violation")]
    [InlineData("advisory", "advisory")]
    public void An_incomplete_rule_preserves_its_findings_and_their_explicit_policy(string mode, string severity)
    {
        using var repository = Application()
            .WriteFile(".harness.json", Frame.AllPresent()
                .Architecture("""{ "standard": "sliced-dotnet/1" }""")
                .Policy(Family + ".zone-shape", mode).ToString())
            .Remove("src/Shop/Host/Program.cs")
            .Commit()
            .PointIndexAtMissingObject("src/Shop/Application/Sales/Create.cs")
            .Remove("src/Shop/Application/Sales/Create.cs");
        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Family + ".zone-shape");
        Assert.Equal(2, run.ExitCode);
        Assert.Contains("outcome: incomplete", run.Output, StringComparison.Ordinal);
        Assert.Contains("missing required layer 'Host'", run.Output, StringComparison.Ordinal);
        Assert.Contains(severity + "  ", run.Output, StringComparison.Ordinal);
        Assert.Contains("architecture map:", run.Output, StringComparison.Ordinal);
    }

    private static RepositoryFixture Application(string mode = "required")
        => Fixtures.Compliant(Frame.AllPresent()
            .Architecture("""{ "standard": "sliced-dotnet/1" }""")
            .Policy(Family + ".segment-names", mode))
            .WriteFile("src/Shop/Host/Program.cs", "sealed class Program;")
            .WriteFile("src/Shop/Application/Sales/Create.cs", "namespace Sales; public class Create;")
            .WriteFile("src/Shop/Application/Sales/Services/Service.cs", "sealed class Service;")
            .WriteFile("src/Shop/Api/Sales/Endpoint.cs", "sealed class Endpoint : Sales.Create;")
            .Commit();
}

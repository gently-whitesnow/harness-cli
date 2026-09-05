using System.Text.Json;

namespace Harness.Tests;

public sealed class InitCommandTests
{
    private static readonly string[] Questions =
        ["tests.unit", "tests.integration", "tests.architecture", "format", "lint", "build", "typecheck", "verify"];

    [Fact]
    public void Init_from_a_subdirectory_creates_an_untracked_unanswered_frame_at_the_git_root()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("src/App.cs", "sealed class App;")
            .Commit();
        Directory.CreateDirectory(repository.Absolute("src/Feature"));

        var run = HarnessCli.RunWithInput(repository.Absolute("src/Feature"), "application\n", "init");

        Assert.Equal(0, run.ExitCode);
        Assert.Empty(run.StandardError);
        Assert.Contains(repository.Absolute(".harness.json"), run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Review every answer", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("ask the repository owner", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Track the file", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("harness check --verbose", run.StandardOutput, StringComparison.Ordinal);

        var path = repository.Absolute(".harness.json");
        Assert.True(File.Exists(path));
        Assert.False(File.Exists(repository.Absolute("src/Feature/.harness.json")));
        Assert.Equal("?? .editorconfig\n?? .harness.budget.json\n?? .harness.json\n", repository.Git("status", "--porcelain=v1"));
        Assert.Contains(".editorconfig' with the shared code-style baseline.", run.StandardOutput, StringComparison.Ordinal);
        Assert.StartsWith("root = true\n", File.ReadAllText(repository.Absolute(".editorconfig")), StringComparison.Ordinal);

        using var document = JsonDocument.Parse(File.ReadAllText(path));
        var root = document.RootElement;
        Assert.Equal(Release.Current, root.GetProperty("version").GetString());
        Assert.Equal("sliced-dotnet/1", root.GetProperty("architecture").GetProperty("standard").GetString());
        Assert.Equal(
            Questions,
            root.GetProperty("answers").EnumerateObject().Select(answer => answer.Name));
        Assert.All(
            root.GetProperty("answers").EnumerateObject(),
            answer => Assert.Empty(answer.Value.EnumerateObject()));
        Assert.True(root.GetProperty("applicability").GetProperty("csharp").GetProperty("applicable").GetBoolean());
        Assert.True(root.GetProperty("applicability").GetProperty("dotnet").GetProperty("applicable").GetBoolean());
        AssertDefaultSettings(root.GetProperty("settings"));
        Assert.Contains("harness-hooks", repository.Git("config", "--local", "--get", "core.hooksPath"));
        Assert.All(root.GetProperty("policy").EnumerateObject(), entry =>
            Assert.Equal(entry.Name == "frame.verify"
                ? "required"
                : entry.Name.StartsWith("frame.", StringComparison.Ordinal)
                    ? "off"
                    : "required", entry.Value.GetString()));
        Assert.True(File.Exists(repository.Absolute(".harness.budget.json")));
        Assert.StartsWith("{\n", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.Contains("\n  \"policy\": {\n", File.ReadAllText(path), StringComparison.Ordinal);
        Assert.False(root.TryGetProperty("suppress", out _));

        repository.CommitAs("chore(harness): инициализировать рамку репозитория");
        Assert.Equal(0, HarnessCli.Run(repository.Path, "check", "--only", "complexity.csharp").ExitCode);
        var budgetUpdate = HarnessCli.Run(repository.Path, "budget", "update");
        Assert.Equal(0, budgetUpdate.ExitCode);
        Assert.Contains("UNCHANGED", budgetUpdate.StandardOutput, StringComparison.Ordinal);
    }

    private static void AssertDefaultSettings(JsonElement settings)
    {
        Assert.Equal(
            [
                "comments.csharp", "comments.yaml", "comments.typescript", "duplication.csharp", "commits",
            ],
            settings.EnumerateObject().Select(section => section.Name));
        AssertSection(settings, "comments.csharp", ("minimumCommentLines", 10), ("percentageLimit", 8));
        AssertSection(settings, "comments.yaml", ("minimumCommentLines", 10), ("percentageLimit", 8));
        AssertSection(settings, "comments.typescript", ("minimumCommentLines", 10), ("percentageLimit", 8));
        AssertSection(settings, "duplication.csharp", ("windowLines", 30), ("minimumTokens", 90));
        Assert.Equal("ru", settings
            .GetProperty("commits").GetProperty("language").GetString());
        Assert.True(settings
            .GetProperty("commits").GetProperty("requireSetup").GetBoolean());
    }

    [Fact]
    public void Latest_writes_the_moving_version_marker()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, "application\n", "init", "--latest");

        Assert.Equal(0, run.ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal("latest", document.RootElement.GetProperty("version").GetString());
    }

    [Fact]
    public void Init_selects_the_commit_language_and_installs_its_template()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, "application\n", "init", "--language", "en");

        Assert.Equal(0, run.ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal("en", document.RootElement.GetProperty("settings")
            .GetProperty("commits").GetProperty("language").GetString());
        var template = repository.Git("config", "--local", "--get", "commit.template").Trim();
        Assert.Contains("Context:", File.ReadAllText(template), StringComparison.Ordinal);
    }

    [Fact]
    public void Library_answer_declares_architecture_not_applicable()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, "library\n", "init");

        Assert.Equal(0, run.ExitCode);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        var architecture = document.RootElement.GetProperty("architecture");
        Assert.False(architecture.GetProperty("applicable").GetBoolean());
        Assert.Equal("standalone library", architecture.GetProperty("reason").GetString());

        repository.CommitAs("chore(harness): инициализировать рамку библиотеки");
        var check = HarnessCli.RunVerbose(repository.Path, "check", "--only", "architecture.sliced-dotnet");
        Assert.Equal(0, check.ExitCode);
        Assert.Contains("not applicable", check.Output, StringComparison.Ordinal);
        Assert.Contains("architecture map: not applicable", check.Output, StringComparison.Ordinal);
    }

    [Fact]
    public void Kind_option_initializes_without_reading_stdin()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init", "--kind", "library");

        Assert.Equal(0, run.ExitCode);
        Assert.DoesNotContain("Repository kind", run.StandardOutput, StringComparison.Ordinal);
        using var document = JsonDocument.Parse(File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.False(document.RootElement.GetProperty("architecture").GetProperty("applicable").GetBoolean());
    }

    [Fact]
    public void Closed_stdin_explains_the_non_interactive_kind_option()
    {
        using var repository = RepositoryFixture.CreateGitRepository();

        var run = HarnessCli.RunWithInput(repository.Path, string.Empty, "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("--kind application", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Canonical_empty_dotnet_application_is_green_after_init()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("AGENTS.md", "# Navigation\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .WriteFile(
                "Directory.Build.props",
                Fixtures.HardenedBuildProps.Replace(
                    "<PropertyGroup>",
                    "<PropertyGroup>\n    <TargetFramework>net10.0</TargetFramework>",
                    StringComparison.Ordinal))
            .WriteFile(
                "App.slnx",
                """
                <Solution>
                  <Project Path="src/App/Host/App.Host.csproj" />
                  <Project Path="src/App/Api/App.Api.csproj" />
                  <Project Path="src/App/Application/App.Application.csproj" />
                </Solution>
                """)
            .WriteFile(
                "src/App/Host/App.Host.csproj",
                LayerProject("../Api/App.Api.csproj", "../Application/App.Application.csproj"))
            .WriteFile("src/App/Api/App.Api.csproj", LayerProject("../Application/App.Application.csproj"))
            .WriteFile("src/App/Application/App.Application.csproj", LayerProject())
            .WriteFile("src/App/Host/Program.cs", "namespace App.Host; sealed class Program;\n")
            .WriteFile(
                "src/App/Api/Example/Endpoint.cs",
                "namespace App.Api.Example; sealed class Endpoint(App.Application.Example.UseCase useCase);\n")
            .WriteFile(
                "src/App/Application/Example/UseCase.cs",
                "namespace App.Application.Example; sealed class UseCase;\n")
            .Commit();

        Assert.Equal(0, HarnessCli.RunWithInput(repository.Path, "application\n", "init").ExitCode);
        var framePath = repository.Absolute(".harness.json");
        File.WriteAllText(
            framePath,
            File.ReadAllText(framePath).Replace(
                "\"verify\": {}",
                "\"verify\": { \"paths\": [\"verify.sh\"] }",
                StringComparison.Ordinal));
        repository.CommitAs("chore(harness): инициализировать рамку репозитория");

        var check = HarnessCli.Run(repository.Path, "check");

        Assert.Equal(0, check.ExitCode);
        Assert.Contains("architecture map: zone src/App", check.Output, StringComparison.Ordinal);
        Assert.Contains("DSM budget:", check.Output, StringComparison.Ordinal);
        Assert.Contains(
            "\"meanReach\": 1,\n",
            File.ReadAllText(repository.Absolute(".harness.budget.json")),
            StringComparison.Ordinal);
    }

    [Fact]
    public void Initialized_frame_requires_verify_and_disables_other_unanswered_questions()
    {
        using var repository = RepositoryFixture.CreateGitRepository();
        Assert.Equal(0, HarnessCli.RunWithInput(repository.Path, "application\n", "init").ExitCode);
        repository.CommitAs("chore(harness): инициализировать рамку репозитория");

        var check = HarnessCli.RunVerbose(repository.Path, "check", "--only", "frame");

        Assert.Equal(2, check.ExitCode);
        foreach (var question in Questions)
        {
            Assert.Contains($"frame.{question}", check.Output, StringComparison.Ordinal);
        }

        Assert.Equal(Questions.Length - 1, Occurrences(check.Output, "outcome: skipped"));
        Assert.Contains("outcome: incomplete", check.Output, StringComparison.Ordinal);
    }

    [Theory]
    [InlineData(false)]
    [InlineData(true)]
    public void Init_never_overwrites_an_existing_frame(bool tracked)
    {
        const string existing = "keep exactly this\n";
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile(".harness.json", existing);
        if (tracked)
        {
            repository.Commit();
        }

        var before = repository.Git("status", "--porcelain=v1");
        var run = HarnessCli.RunWithInput(repository.Path, "application\n", "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Repository kind", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("Refusing to overwrite", run.StandardError, StringComparison.Ordinal);
        Assert.Equal(existing, File.ReadAllText(repository.Absolute(".harness.json")));
        Assert.Equal(before, repository.Git("status", "--porcelain=v1"));
    }

    [Fact]
    public void Init_does_not_replace_a_tracked_frame_deleted_from_the_working_tree()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile(".harness.json", "tracked\n")
            .Commit()
            .Remove(".harness.json");

        var run = HarnessCli.RunWithInput(repository.Path, "application\n", "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Refusing to overwrite", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Init_does_not_replace_a_tracked_budget_deleted_from_the_working_tree()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile(".harness.budget.json", "tracked\n")
            .Commit()
            .Remove(".harness.budget.json");

        var run = HarnessCli.RunWithInput(repository.Path, "application\n", "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Refusing to overwrite", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.budget.json")));
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    [Fact]
    public void Init_outside_git_fails_without_creating_a_frame()
    {
        using var directory = TemporaryDirectory.Create();

        var run = HarnessCli.RunWithInput(directory.Path, "application\n", "init");

        Assert.Equal(2, run.ExitCode);
        Assert.Contains("Repository kind", run.StandardOutput, StringComparison.Ordinal);
        Assert.Contains("not inside a Git repository", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(directory.Absolute(".harness.json")));
    }

    [Theory]
    [InlineData("--force")]
    [InlineData("--language", "de")]
    [InlineData("--latest", "--latest")]
    [InlineData("--kind", "service")]
    [InlineData("--kind", "application", "--kind", "library")]
    [InlineData("one", "two")]
    public void Invalid_init_arguments_show_usage_and_fail(params string[] arguments)
    {
        using var repository = RepositoryFixture.CreateGitRepository();
        var command = arguments.Prepend("init").ToArray();

        var run = HarnessCli.Run(repository.Path, command);

        Assert.Equal(2, run.ExitCode);
        Assert.Empty(run.StandardOutput);
        Assert.Contains("harness init [path] [--kind", run.StandardError, StringComparison.Ordinal);
        Assert.False(File.Exists(repository.Absolute(".harness.json")));
    }

    private static int Occurrences(string text, string value)
        => text.Split(value, StringSplitOptions.None).Length - 1;

    // ADR-0041: the canonical application builds every layer as its own project; the shared
    // TargetFramework lives in Directory.Build.props.
    private static string LayerProject(params string[] references)
        => "<Project Sdk=\"Microsoft.NET.Sdk\">\n  <ItemGroup>\n"
            + string.Concat(references.Select(reference =>
                $"    <ProjectReference Include=\"{reference}\" />\n"))
            + "  </ItemGroup>\n</Project>\n";

    private static void AssertSection(
        JsonElement settings,
        string sectionName,
        params (string Name, int Value)[] expected)
    {
        var section = settings.GetProperty(sectionName);
        Assert.Equal(expected.Select(item => item.Name), section.EnumerateObject().Select(item => item.Name));
        foreach (var (name, value) in expected)
        {
            Assert.Equal(value, section.GetProperty(name).GetInt32());
        }
    }
}

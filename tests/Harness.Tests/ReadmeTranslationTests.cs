namespace Harness.Tests;

/// <summary>
/// ADR-0068: a README translation named by a language tag lives beside the README.md it
/// translates and carries the same line limit; the same name without an original is a finding.
/// </summary>
public sealed class ReadmeTranslationTests
{
    [Theory]
    [InlineData("README.ru.md")]
    [InlineData("README.zh-CN.md")]
    [InlineData("README.pt-BR.md")]
    [InlineData("README.zh-Hans.md")]
    public void Translation_beside_the_root_readme_is_allowed(string path)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(path, "# Обзор\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains(path), run.Output);
    }

    [Fact]
    public void Nested_translation_beside_a_nested_readme_is_allowed()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tools/README.md", "# Tools\n")
            .WriteFile("tools/README.ru.md", "# Инструменты\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Translation_without_an_original_is_a_finding()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tools/README.ru.md", "# Инструменты\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("tools/README.ru.md"), run.Output);
        Assert.True(run.OutputContains("no README.md tracked beside it"), run.Output);
    }

    [Fact]
    public void Translation_carries_the_readme_line_limit()
    {
        using var repository = Fixtures.Compliant()
            .WriteLines("README.ru.md", 151)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("README.ru.md"), run.Output);
        Assert.True(run.OutputContains("exceeds the limit of 150"), run.Output);
    }

    [Theory]
    [InlineData("README.russian.md")]
    [InlineData("README-ru.md")]
    [InlineData("README.RU.md")]
    [InlineData("README.zh_CN.md")]
    [InlineData("readme.ru.md")]
    public void Name_that_is_not_a_language_tag_is_still_unexpected(string path)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile(path, "# Text\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "docs.policy");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("unexpected tracked Markdown"), run.Output);
    }

    [Fact]
    public void Explanation_and_upgrade_route_name_translations()
    {
        using var repository = Fixtures.WithRawFrame("""
            { "version": "3.8.0", "settings": {}, "policy": { "docs.policy": "required" } }
            """).Commit();

        var explain = HarnessCli.Run(repository.Path, "explain", "docs.policy");
        var upgrade = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.True(explain.OutputContains("README.<language>.md"), explain.Output);
        Assert.Equal(0, upgrade.ExitCode);
        Assert.True(upgrade.OutputContains("Release 3.9 additions"), upgrade.Output);
    }
}

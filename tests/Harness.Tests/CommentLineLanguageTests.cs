namespace Harness.Tests;

/// <summary>
/// The comment density rule read through the YAML and TypeScript readers. In YAML a comment
/// block above a key of any depth, a trailing comment and a directive are not prose (ADR-0056).
/// </summary>
public sealed class CommentLineLanguageTests
{
    private const string Yaml = "comments.yaml";

    private const string TypeScript = "comments.typescript";

    [Fact]
    public void A_repository_without_yaml_or_typescript_is_not_applicable_to_both()
    {
        using var repository = Fixtures.Compliant();

        var yaml = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);
        var typescript = HarnessCli.RunVerbose(repository.Path, "check", "--only", TypeScript);

        Assert.Equal(0, yaml.ExitCode);
        Assert.True(yaml.OutputContains("not applicable"), yaml.Output);
        Assert.Equal(0, typescript.ExitCode);
        Assert.True(typescript.OutputContains("not applicable"), typescript.Output);
    }

    [Fact]
    public void A_yaml_file_above_the_limit_is_a_blocking_violation()
    {
        var source = Lines(100, line => $"key{line}: value") + Lines(12, line => $"# free prose {line}");
        using var repository = Fixtures.Compliant().WriteFile("deploy/values.yml", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("deploy/values.yml"), run.Output);
        Assert.True(run.OutputContains("12 of 112 authored physical lines"), run.Output);
        Assert.True(run.OutputContains("8% limit"), run.Output);
    }

    [Fact]
    public void Comment_blocks_above_keys_of_any_depth_trailing_comments_and_directives_are_not_prose()
    {
        var source = Lines(12, line => $"# the schema of the catalogue, line {line}")
            + "\n---\n"
            + Lines(12, line => $"# what the top key means, line {line}")
            + "\nservices:\n"
            + Lines(12, line => $"  # why the nested value is what it is, line {line}")
            + "  web:\n    image: \"registry/web@sha256:0000\"   # 1.2.3\n"
            + "    ports:\n      # the api port\n      - 8080\n"
            + "# yamllint disable-line rule:line-length\n"
            + "    note: value # noqa: yaml[line-length]\n";
        using var repository = Fixtures.Compliant().WriteFile("group_vars/all/services.yml", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("outcome: passed"), run.Output);
    }

    [Fact]
    public void Only_a_block_followed_by_another_block_or_the_end_of_the_file_is_prose()
    {
        const string source = "# header, first line\n# header, second line\n\n# documents the key below\ntop:\n"
            + "  nested: value   # trailing\n  list:\n    # documents the item\n    - one\n"
            + "# yamllint disable-line\n"
            + "#  - name: commented out task\n#    hosts: all\n#    tasks: []\n\n# a closing thought\n";
        using var repository = Fixtures.Compliant(Frame.AllPresent().Settings(
                """{ "comments.yaml": { "percentageLimit": 0, "minimumCommentLines": 1 } }"""))
            .WriteFile("playbook.yml", source)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("6 of 10 authored physical lines"), run.Output);
    }

    [Fact]
    public void Init_writes_comments_yaml_as_advisory_only_without_an_application_language()
    {
        using var configuration = RepositoryFixture.CreateGitRepository().WriteFile("deploy/values.yml", "key: value\n").Commit();
        using var application = RepositoryFixture.CreateGitRepository()
            .WriteFile("deploy/values.yml", "key: value\n")
            .WriteFile("go.mod", "module example.com/app\n\ngo 1.25\n")
            .WriteFile("main.go", "package main\n\nfunc main() {}\n")
            .Commit();

        Assert.Equal(0, HarnessCli.RunWithInput(configuration.Path, string.Empty, "init").ExitCode);
        Assert.Equal(0, HarnessCli.RunWithInput(application.Path, string.Empty, "init").ExitCode);

        Assert.Contains("\"comments.yaml\": \"advisory\"", File.ReadAllText(configuration.Absolute(".harness.json")), StringComparison.Ordinal);
        Assert.Contains("\"comments.yaml\": \"required\"", File.ReadAllText(application.Absolute(".harness.json")), StringComparison.Ordinal);
    }

    [Fact]
    public void Hash_inside_quoted_and_block_scalars_is_yaml_content()
    {
        var source = Lines(10, line => $"quoted{line}: \"channel #{line}\" # trailing")
            + "script: |\n"
            + Lines(30, line => $"  # not a comment {line}")
            + "single: 'it''s #{tag}'\n"
            + Lines(100, line => $"key{line}: value");
        using var repository = Fixtures.Compliant().WriteFile("ci.yaml", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("comments.yaml"), run.Output);
    }

    [Fact]
    public void A_typescript_file_above_the_limit_is_a_blocking_violation()
    {
        var source = Lines(9, line => $"// reason {line}")
            + "/** contract */ export const value = 1; // reason\n"
            + Lines(100, line => $"export const value{line} = {line};");
        using var repository = Fixtures.Compliant().WriteFile("web/src/model.ts", source).Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", TypeScript);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("web/src/model.ts"), run.Output);
        Assert.True(run.OutputContains("10 of 110 authored physical lines"), run.Output);
    }

    [Fact]
    public void Comment_shapes_inside_typescript_literals_do_not_count()
    {
        var source = Lines(10, line => $"const url{line} = 'http://host/{line}'; // reason")
            + Lines(20, line => $"const pattern{line} = /\\/\\/{line}/g;")
            + Lines(20, line => $"const text{line} = `/* ${{value{line}}} */ // not`;")
            + "const wide = `\n// still inside the template\n\n// and here\n`;\n"
            + Lines(70, line => $"const value{line} = total / count{line} / 2;");
        using var repository = Fixtures.Compliant(Frame.AllPresent().Settings(
                """{ "comments.typescript": { "percentageLimit": 7, "minimumCommentLines": 10 } }"""))
            .WriteFile("web/src/literals.tsx", source)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", TypeScript);

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("10 of 124 authored physical lines"), run.Output);
    }

    [Fact]
    public void Declaration_files_and_generated_bundles_are_excluded()
    {
        var dense = Lines(20, line => $"// reason {line}") + "export const value = 1;\n";
        using var repository = Fixtures.Compliant()
            .WriteFile("web/types.d.ts", dense)
            .WriteFile("web/dist/app.js", dense)
            .WriteFile("web/vendor.min.js", dense)
            .WriteFile("web/api.ts", "/* @generated by the contract build */\n" + dense)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", TypeScript);

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("not applicable"), run.Output);
    }

    [Fact]
    public void Each_language_reads_its_own_settings_section()
    {
        var source = Lines(12, line => $"# reason {line}") + Lines(30, line => $"key{line}: value");
        using var repository = Fixtures.Compliant(Frame.AllPresent().Settings(
                """{ "comments.yaml": { "percentageLimit": 30, "minimumCommentLines": 10 } }"""))
            .WriteFile("deploy/values.yml", source)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", Yaml);

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Explain_names_the_reader_and_the_settings_of_each_language()
    {
        using var repository = Fixtures.Compliant();

        var yaml = HarnessCli.Run(repository.Path, "explain", Yaml);
        var typescript = HarnessCli.Run(repository.Path, "explain", TypeScript);

        Assert.Equal(0, yaml.ExitCode);
        Assert.True(yaml.OutputContains("block scalar"), yaml.Output);
        Assert.True(yaml.OutputContains("directly above a key passes as documentation"), yaml.Output);
        Assert.True(yaml.OutputContains("settings.comments.yaml"), yaml.Output);
        Assert.Equal(0, typescript.ExitCode);
        Assert.True(typescript.OutputContains(".d.ts"), typescript.Output);
        Assert.True(typescript.OutputContains("settings.comments.typescript"), typescript.Output);
    }

    private static string Lines(int count, Func<int, string> line)
        => string.Concat(Enumerable.Range(1, count).Select(number => line(number) + "\n"));
}

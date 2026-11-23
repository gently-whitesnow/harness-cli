namespace Harness.Tests;

public sealed class TypeScriptHygieneTests
{
    [Fact]
    public void Strict_tsconfig_covering_sources_passes()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.json", """{ "compilerOptions": { "strict": true } }""")
            .WriteFile("src/app.ts", "export const value: number = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "tsconfig.typescript");

        Assert.Equal(0, run.ExitCode);
    }

    [Fact]
    public void Pure_javascript_repository_marks_tsconfig_not_applicable()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("site/app.js", "export const value = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "tsconfig.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("no TypeScript sources"), run.Output);
    }

    [Fact]
    public void Missing_tsconfig_for_typescript_source_blocks()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/app.ts", "export const value: number = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "tsconfig.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/app.ts"), run.Output);
        Assert.True(run.OutputContains("no tracked tsconfig.json"), run.Output);
    }

    [Fact]
    public void Missing_strict_mode_or_weakened_flags_block()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.json", """{ "files": [], "references": [{ "path": "./tsconfig.app.json" }, { "path": "./tsconfig.node.json" }] }""")
            .WriteFile("tsconfig.app.json", """{ "compilerOptions": { "strict": true, "noImplicitAny": false } }""")
            .WriteFile("tsconfig.node.json", """{ "compilerOptions": { "composite": true } }""")
            .WriteFile("src/app.ts", "export const value: number = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "tsconfig.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("tsconfig.app.json") && run.OutputContains("noImplicitAny"), run.Output);
        Assert.True(run.OutputContains("tsconfig.node.json") && run.OutputContains("strict: true"), run.Output);
    }

    [Fact]
    public void Local_extends_chain_inherits_strict_mode()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.base.json", """{ "compilerOptions": { "strict": true } }""")
            .WriteFile("apps/web/tsconfig.json", """{ "extends": "../../tsconfig.base.json" }""")
            .WriteFile("apps/web/src/app.ts", "export const value: number = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "tsconfig.typescript");

        Assert.Equal(0, run.ExitCode);
    }

    [Theory]
    [InlineData("// @ts-ignore\nexport const x = 1;\n", "@ts-ignore")]
    [InlineData("// @ts-nocheck\nexport const x = 1;\n", "@ts-nocheck")]
    [InlineData("// @ts-expect-error legacy\nexport const x = 1;\n", "@ts-expect-error")]
    [InlineData("// eslint-disable-next-line no-console\nexport const x = 1;\n", "eslint-disable-next-line")]
    [InlineData("/* eslint-disable */\nexport const x = 1;\n", "eslint-disable")]
    [InlineData("// biome-ignore lint/suspicious/noExplicitAny: reason\nexport const x = 1;\n", "biome-ignore")]
    [InlineData("// oxlint-disable-next-line no-unused-vars\nexport const x = 1;\n", "oxlint-disable-next-line")]
    public void Lint_suppression_comment_blocks(string source, string directive)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/app.ts", source)
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "lint-suppressions.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/app.ts:1"), run.Output);
        Assert.True(run.OutputContains(directive), run.Output);
    }

    [Fact]
    public void Suppression_text_inside_string_literals_does_not_block()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/app.ts", "export const doc = \"// @ts-ignore eslint-disable-next-line biome-ignore\";\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "lint-suppressions.typescript");

        Assert.Equal(0, run.ExitCode);
    }
}

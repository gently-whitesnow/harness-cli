namespace Harness.Tests;

public sealed class TypeScriptLanguageTests
{
    [Fact]
    public void Literal_imports_prove_directory_cycles_including_type_only_edges()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/model/a.ts", "import type { B } from '../ui/b.js';\nexport type A = B;\n")
            .WriteFile("src/ui/b.ts", "import type { A } from '../model/a.js';\nexport type B = A;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("dependency cycle"), run.Output);
        Assert.True(run.OutputContains("type-only"), run.Output);
    }

    [Fact]
    public void Tracked_tsconfig_aliases_and_package_dependencies_are_distinguished()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.json", "{ // comment\n \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@/*\": [\"src/*\"] } }, \"include\": [\"src\"] }")
            .WriteFile("package.json", "{\"dependencies\":{\"react\":\"*\"}}")
            .WriteFile("src/a/main.ts", "import { x } from '@/b/x';\nimport React from 'react';\nimport { missing } from 'absent';\n")
            .WriteFile("src/b/x.ts", "export const x = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("unresolved import: src/a/main.ts:3 absent"), run.Output);
        Assert.False(run.OutputContains("unresolved import: src/a/main.ts:1"), run.Output);
        Assert.False(run.OutputContains("unresolved import: src/a/main.ts:2"), run.Output);
    }

    [Fact]
    public void Named_barrel_import_reaches_only_its_exported_file()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", "export { Button } from './button';\nexport { Icon } from './icon';\n")
            .WriteFile("src/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/ui/icon.ts", "export const Icon = 1;\n")
            .WriteFile("src/app/main.ts", "import { Button } from '../ui';\nexport const x = Button;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("3 authored TypeScript/JavaScript files"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.33 files"), run.Output);
    }
    [Fact]
    public void Duplicated_typescript_blocks_are_measured_across_files()
    {
        var block = string.Concat(Enumerable.Range(0, 35).Select(i => $"const value{i} = other{i} {string.Concat(Enumerable.Repeat("+ 1 ", i + 1))};\n"));
        using var repository = Fixtures.Compliant()
            .WriteFile("src/one.ts", block)
            .WriteFile("src/two.ts", block.Replace("value", "item", StringComparison.Ordinal))
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "duplication.typescript");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("src/one.ts:"), run.Output);
        Assert.True(run.OutputContains("src/two.ts:"), run.Output);
    }

    [Fact]
    public void Invalid_tsconfig_with_a_bare_import_is_incomplete()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.json", "{ invalid }")
            .WriteFile("src/app.ts", "import { Widget } from '@widgets/core';\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");

        Assert.Equal(2, run.ExitCode);
        Assert.True(run.OutputContains("tsconfig.json"), run.Output);
    }

    [Fact]
    public void Namespace_import_through_barrel_reaches_every_reexport()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", "export * from './button';\nexport * from './icon';\n")
            .WriteFile("src/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/ui/icon.ts", "export const Icon = 1;\n")
            .WriteFile("src/app/main.ts", "import * as UI from '../ui';\nexport const x = UI.Button;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("average reachable files: 1.67 files"), run.Output);
    }

    [Fact]
    public void Init_writes_all_typescript_sections()
    {
        using var repository = RepositoryFixture.CreateGitRepository()
            .WriteFile("src/app.ts", "export const answer = 42;\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "init");

        Assert.Equal(0, run.ExitCode);
        var frame = File.ReadAllText(repository.Absolute(".harness.json"));
        Assert.Contains("dependencies.typescript", frame, StringComparison.Ordinal);
        Assert.Contains("duplication.typescript", frame, StringComparison.Ordinal);
        Assert.Contains("complexity.typescript", frame, StringComparison.Ordinal);
        Assert.Contains("12.0", frame, StringComparison.Ordinal);
    }

    [Fact]
    public void A_barrel_with_code_remains_a_dsm_node()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", "export { Button } from './button';\nexport const local = 1;\n")
            .WriteFile("src/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/app/main.ts", "import { Button } from '../ui';\nexport const x = Button;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("3 authored TypeScript/JavaScript files"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 2.00 files"), run.Output);
    }

    [Fact]
    public void Dynamic_imports_and_import_text_inside_literals_do_not_create_edges()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/app.ts", "const text = \"import './fake'\";\nconst pattern = /import\\('fake'\\)/;\nimport(variable);\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("unresolved: 1"), run.Output);
        Assert.True(run.OutputContains("<dynamic>"), run.Output);
        Assert.False(run.OutputContains("fake"), run.Output);
    }

    [Fact]
    public void Same_directory_file_import_cycle_is_not_a_directory_cycle()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/a.ts", "import { B } from './b';\nexport const A = B;\n")
            .WriteFile("src/ui/b.ts", "import { A } from './a';\nexport const B = A;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.False(run.OutputContains("dependency cycle"), run.Output);
    }

    [Fact]
    public void Workspace_package_and_imports_alias_resolve_to_tracked_files()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("packages/core/package.json", "{\"name\":\"@example/core\",\"imports\":{\"#internal/*\":\"./src/*\"}}")
            .WriteFile("packages/core/src/model.ts", "export const Model = 1;\n")
            .WriteFile("packages/core/src/api.ts", "import { Model } from '#internal/model';\nexport const Api = Model;\n")
            .WriteFile("src/app.ts", "import { Api } from '@example/core/src/api';\nexport const app = Api;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("unresolved: 0"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 2.00 files"), run.Output);
    }

    [Fact]
    public void Multiline_imports_and_exports_resolve()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", "export {\n Button\n} from './button';\n")
            .WriteFile("src/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/app/main.ts", "import {\n Button\n} from '../ui';\nexport const x = Button;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("transparent barrels: 1"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.50 files"), run.Output);
    }

    [Fact]
    public void Extended_tsconfig_selects_paths_for_included_sources()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.base.json", "{\"compilerOptions\":{\"baseUrl\":\".\",\"paths\":{\"@shared/*\":[\"src/shared/*\"]}}}")
            .WriteFile("apps/web/tsconfig.json", "{\"extends\":\"../../tsconfig.base.json\",\"include\":[\"src\"]}")
            .WriteFile("apps/web/src/app.ts", "import { value } from '@shared/value';\nexport const app = value;\n")
            .WriteFile("src/shared/value.ts", "export const value = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.True(run.ExitCode == 0, run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.50 files"), run.Output);
        Assert.True(run.OutputContains("unresolved: 0"), run.Output);
    }

    [Fact]
    public void Coverage_prints_the_missing_typescript_axis_fragment()
    {
        const string frame = """
            { "version": "latest", "settings": { "commits": { "language": "ru", "requireSetup": false } },
              "policy": { "harness.coverage": "required" } }
            """;
        using var repository = Fixtures.WithRawFrame(frame)
            .WriteFile("src/app.ts", "export const app = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "harness.coverage");

        Assert.Equal(1, run.ExitCode);
        Assert.True(run.OutputContains("duplication.typescript"), run.Output);
        Assert.True(run.OutputContains("complexity.typescript"), run.Output);
        Assert.True(run.OutputContains("dependencies.typescript"), run.Output);
    }

    [Fact]
    public void Upgrade_from_35_names_the_typescript_checks_and_ceiling()
    {
        using var repository = Fixtures.Compliant(Frame.AllPresent().Version("3.5.0"))
            .WriteFile("src/app.ts", "export const app = 1;\n")
            .Commit();

        var run = HarnessCli.Run(repository.Path, "upgrade", "--dry-run");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("Release 3.6 additions"), run.Output);
        Assert.True(run.OutputContains("dependencies.typescript"), run.Output);
        Assert.True(run.OutputContains("12.0 / 0"), run.Output);
    }

    [Fact]
    public void Workspace_package_exports_and_conditional_imports_resolve()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("packages/lib/package.json", "{\"name\":\"@example/lib\",\"exports\":{\".\":{\"import\":\"./src/index.js\"}},\"imports\":{\"#value\":{\"default\":\"./src/value.ts\"}}}")
            .WriteFile("packages/lib/src/index.ts", "import { value } from '#value';\nexport const answer = value;\n")
            .WriteFile("packages/lib/src/value.ts", "export const value = 42;\n")
            .WriteFile("src/app.ts", "import { answer } from '@example/lib';\nexport const app = answer;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("average reachable files: 2.00 files"), run.Output);
        Assert.True(run.OutputContains("unresolved: 0"), run.Output);
    }

    [Fact]
    public void Reexport_aliases_follow_the_named_definition_through_two_barrels()
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", "export { Button as Primary } from './middle';\nexport { Icon } from './icon';\n")
            .WriteFile("src/ui/middle.ts", "import { Button as Local } from './button';\nexport { Local as Button };\n")
            .WriteFile("src/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/ui/icon.ts", "export const Icon = 1;\n")
            .WriteFile("src/app/main.ts", "import { Primary } from '../ui';\nexport const x = Primary;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("transparent barrels: 2"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.33 files"), run.Output);
    }

}

namespace Harness.Tests;

/// <summary>Every named specifier of an import or reexport resolves through barrels on its own.</summary>
public sealed class TypeScriptBarrelNameTests
{
    private const string OneNamePerLine = "export { Button } from './button';\nexport { default } from './button';\n"
        + "export { Icon } from './icon';\nexport { Card } from './card';\nexport { Modal } from './modal';\n";

    [Theory]
    [InlineData("import { Button } from '../ui';\nimport { Icon } from '../ui';\n")]
    [InlineData("import { Button, Icon } from '../ui';\n")]
    [InlineData("import { Button as Primary, Icon as Glyph } from '../ui';\n")]
    [InlineData("import { type Button, Icon } from '../ui';\n")]
    [InlineData("import type { Button, Icon } from '../ui';\n")]
    [InlineData("import Primary, { Icon } from '../ui';\n")]
    [InlineData("import { default as Primary, Icon } from '../ui';\n")]
    [InlineData("import {\n  Button, // primary\n  /* glyph, */ Icon,\n} from '../ui';\n")]
    [InlineData("export { Button, Icon as Glyph } from '../ui';\n")]
    public void Each_imported_name_reaches_only_its_module(string imports)
    {
        using var repository = UiRepository(OneNamePerLine, imports + "export const x = 1;\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("transparent barrels: 1"), run.Output);
        Assert.True(run.OutputContains("5 authored TypeScript/JavaScript files"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.40 files"), run.Output);
    }

    [Theory]
    [InlineData(OneNamePerLine)]
    [InlineData("export { Button, default } from './button';\nexport { Icon } from './icon';\nexport { Card } from './card';\nexport { Modal } from './modal';\n")]
    [InlineData("export {\n  Button, // primary\n  default,\n} from './button';\nexport { Icon, Icon as Glyph } from './icon';\nexport type { Card } from './card';\nexport { type Modal } from './modal';\n")]
    [InlineData("import Primary, { Button } from './button';\nimport { Icon } from './icon';\nimport { Card } from './card';\nimport { Modal } from './modal';\nexport { Button, Primary as default, Icon, Card, Modal };\n")]
    public void Multi_name_barrel_matches_one_name_per_line(string barrel)
    {
        using var repository = UiRepository(barrel, "import { Button } from '../ui';\nimport { Icon } from '../ui';\nexport const x = 1;\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("transparent barrels: 1"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.40 files"), run.Output);
    }

    [Theory]
    [InlineData("import * as UI from '../ui';\n")]
    [InlineData("import { Button, Missing } from '../ui';\n")]
    public void Namespace_or_unknown_name_reaches_the_whole_barrel(string imports)
    {
        using var repository = UiRepository(OneNamePerLine, imports + "export const x = 1;\n");

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("average reachable files: 1.80 files"), run.Output);
    }

    [Fact]
    public void Namespace_reexport_is_reached_by_its_exposed_name()
    {
        using var repository = UiRepository("export * as Controls from './controls';\nexport { Card } from './card';\n",
                "import { Card } from '../ui';\nexport const x = Card;\n")
            .WriteFile("src/ui/controls.ts", "export const Toggle = 1;\n")
            .Commit();

        var run = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, run.ExitCode);
        Assert.True(run.OutputContains("6 authored TypeScript/JavaScript files"), run.Output);
        Assert.True(run.OutputContains("average reachable files: 1.17 files"), run.Output);
    }

    [Theory]
    [InlineData("import { Button } from '@/shared';\nimport { Icon } from '@/shared';\n")]
    [InlineData("import { Button, Icon } from '@/shared';\n")]
    public void Multi_name_import_through_nested_barrels_proves_no_false_cycle(string imports)
    {
        using var repository = Fixtures.Compliant()
            .WriteFile("tsconfig.json", "{ \"compilerOptions\": { \"baseUrl\": \".\", \"paths\": { \"@/*\": [\"src/*\"] } }, \"include\": [\"src\"] }")
            .WriteFile("src/shared/index.ts", "export { Button, Icon } from './ui';\nexport { store } from './model';\n")
            .WriteFile("src/shared/ui/index.ts", "export { Button } from './button';\nexport { Icon } from './icon';\n")
            .WriteFile("src/shared/ui/button.ts", "export const Button = 1;\n")
            .WriteFile("src/shared/ui/icon.ts", "export const Icon = 1;\n")
            .WriteFile("src/shared/model/index.ts", "export { store } from './store';\n")
            .WriteFile("src/shared/model/store.ts", "import { app } from '../../app/main';\nexport const store = app;\n")
            .WriteFile("src/app/main.ts", imports + "export const app = 1;\n")
            .Commit();

        var dependencies = HarnessCli.RunVerbose(repository.Path, "check", "--only", "dependencies.typescript");
        var complexity = HarnessCli.RunVerbose(repository.Path, "check", "--only", "complexity.typescript");

        Assert.Equal(0, dependencies.ExitCode);
        Assert.False(dependencies.OutputContains("dependency cycle"), dependencies.Output);
        Assert.Equal(0, complexity.ExitCode);
        Assert.True(complexity.OutputContains("transparent barrels: 3"), complexity.Output);
        Assert.True(complexity.OutputContains("average reachable files: 2.25 files"), complexity.Output);
    }

    private static RepositoryFixture UiRepository(string barrel, string main)
        => Fixtures.Compliant()
            .WriteFile("src/ui/index.ts", barrel)
            .WriteFile("src/ui/button.ts", "export const Button = 1;\nexport default Button;\n")
            .WriteFile("src/ui/icon.ts", "export const Icon = 1;\n")
            .WriteFile("src/ui/card.ts", "export const Card = 1;\n")
            .WriteFile("src/ui/modal.ts", "export const Modal = 1;\n")
            .WriteFile("src/app/main.ts", main)
            .Commit();
}

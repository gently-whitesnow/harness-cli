namespace Harness.Tests;

/// <summary>Repository shapes shared by several acceptance tests.</summary>
public static class Fixtures
{
    /// <summary>The framework the generated .NET fixtures target; it must match this repository's SDK.</summary>
    private const string TargetFramework = "net10.0";

    /// <summary>A repository that satisfies the documentation policy in full.</summary>
    public static RepositoryFixture Compliant()
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("ROOT.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("AGENTS.md", "ROOT.md")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .Commit();

    /// <summary>
    /// A compliant repository that also holds one buildable, correctly formatted .NET
    /// library project and no test project.
    /// </summary>
    public static RepositoryFixture DotNetLibrary()
        => Compliant()
            .WriteFile(".gitignore", "bin/\nobj/\n")
            .WriteFile("src/App/App.csproj", Library)
            .WriteFile("src/App/Widget.cs", FormattedSource)
            .Commit();

    /// <summary>A compliant .NET repository whose single test project passes.</summary>
    public static RepositoryFixture DotNetWithPassingTests()
        => DotNetLibrary()
            .WriteFile("tests/App.Tests/App.Tests.csproj", TestProject)
            .WriteFile("tests/App.Tests/WidgetTests.cs", PassingTest)
            .Commit();

    /// <summary>
    /// A compliant repository that also holds one web application: an npm lockfile as the
    /// package-manager evidence, and the standard non-mutating quality scripts. The scripts
    /// run `node` alone, so the fixture exercises real package-manager invocation without
    /// requiring an install the harness is not allowed to perform.
    /// </summary>
    public static RepositoryFixture WebApplication()
        => Compliant()
            .WriteFile(".gitignore", "node_modules/\ndist/\n")
            .WriteFile("package.json", WebManifest)
            .WriteFile("package-lock.json", NpmLockFile)
            .WriteFile("src/main.ts", "export const main = (): number => 0;\n")
            .Commit();

    public const string WebManifest =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "format:check": "node -e \"process.exit(0)\"",
            "lint": "node -e \"process.exit(0)\"",
            "typecheck": "node -e \"process.exit(0)\"",
            "test": "node -e \"process.exit(0)\"",
            "build": "node -e \"process.exit(0)\""
          }
        }

        """;

    /// <summary>The standard scripts plus the manifest's own `packageManager` declaration.</summary>
    public static string WebManifestDeclaring(string packageManager)
        => WebManifest.Replace(
            "\"name\": \"web-fixture\",",
            $"\"name\": \"web-fixture\",\n  \"packageManager\": \"{packageManager}\",",
            StringComparison.Ordinal);

    public const string WebManifestWithFailingLint =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "lint": "node -e \"console.log('src/main.ts: 1 problem')\"; exit 1"
          }
        }

        """;

    /// <summary>A typecheck script that reports a diagnostic in the shape the compiler uses.</summary>
    public const string WebManifestWithFailingTypecheck =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "typecheck": "node -e \"console.log('src/main.ts(3,5): error TS2322: Type string is not assignable to type number.')\"; exit 2"
          }
        }

        """;

    /// <summary>Every gate present and every one of them proving a defect.</summary>
    public const string WebManifestWithFailingScripts =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "format:check": "node -e \"console.log('src/main.ts would be reformatted')\"; exit 1",
            "test": "node -e \"console.log('1 test failed')\"; exit 1",
            "build": "node -e \"console.log('build error: could not resolve ./missing')\"; exit 1"
          }
        }

        """;

    /// <summary>
    /// Formatting offered only through a script that delegates to the fixer, which the
    /// harness must not run merely because the delegation hides the flag.
    /// </summary>
    public const string WebManifestWithDelegatedMutatingFormat =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "format:check": "npm run format:fix",
            "format:fix": "prettier --write ."
          }
        }

        """;

    public const string WebManifestWithoutTypecheck =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "lint": "node -e \"process.exit(0)\"",
            "build": "node -e \"process.exit(0)\""
          }
        }

        """;

    /// <summary>Formatting offered only as a fix, which is not a verification the harness may run.</summary>
    public const string WebManifestWithMutatingFormat =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "format": "prettier --write ."
          }
        }

        """;

    public const string WebManifestWithDependencies =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "build": "node -e \"process.exit(0)\""
          },
          "devDependencies": {
            "typescript": "^5.6.0"
          }
        }

        """;

    /// <summary>A valid npm lockfile for a project with no dependencies.</summary>
    public const string NpmLockFile =
        """
        {
          "name": "web-fixture",
          "lockfileVersion": 3,
          "requires": true,
          "packages": {}
        }

        """;

    public const string PnpmLockFile =
        """
        lockfileVersion: '9.0'

        settings:
          autoInstallPeers: true

        """;

    private const string Library =
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <Nullable>enable</Nullable>
          </PropertyGroup>
        </Project>

        """;

    /// <summary>
    /// Mirrors this repository's own test stack, so the fixture restores from the same
    /// packages the suite already needs rather than requiring a wider environment.
    /// </summary>
    private const string TestProject =
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
            <Nullable>enable</Nullable>
            <IsPackable>false</IsPackable>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="xunit" Version="2.9.3" />
            <PackageReference Include="xunit.runner.visualstudio" Version="3.1.4" />
          </ItemGroup>
          <ItemGroup>
            <ProjectReference Include="../../src/App/App.csproj" />
          </ItemGroup>
        </Project>

        """;

    /// <summary>A project whose only package cannot be resolved from any feed.</summary>
    public const string LibraryNeedingAnUnavailablePackage =
        $"""
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>{TargetFramework}</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Harness.Fixture.NoSuchPackage" Version="1.0.0" />
          </ItemGroup>
        </Project>

        """;

    /// <summary>Removes every inherited feed, so restore has nowhere to look.</summary>
    public const string NoPackageSources =
        """
        <?xml version="1.0" encoding="utf-8"?>
        <configuration>
          <packageSources>
            <clear />
          </packageSources>
        </configuration>

        """;

    /// <summary>A valid lock file for a project with no package references.</summary>
    public const string EmptyLockFile =
        """
        {
          "version": 1,
          "dependencies": {
            ".NETCoreApp,Version=v10.0": {}
          }
        }

        """;

    public const string FormattedSource =
        """
        namespace App;

        public static class Widget
        {
            public static int Size() => 1;
        }

        """;

    /// <summary>Valid C# that `dotnet format --verify-no-changes` rejects.</summary>
    public const string MisformattedSource =
        "namespace App;\n\npublic static class Widget\n{\n        public static int Size() => 1;\n}\n";

    /// <summary>C# that does not compile.</summary>
    public const string UncompilableSource =
        "namespace App;\n\npublic static class Widget\n{\n    public static int Size() => \"one\";\n}\n";

    private const string PassingTest =
        """
        namespace App.Tests;

        public sealed class WidgetTests
        {
            [Xunit.Fact]
            public void Widget_has_a_size() => Xunit.Assert.Equal(1, App.Widget.Size());
        }

        """;

    public const string FailingTest =
        """
        namespace App.Tests;

        public sealed class WidgetTests
        {
            [Xunit.Fact]
            public void Widget_has_a_size() => Xunit.Assert.Equal(99, App.Widget.Size());
        }

        """;
}

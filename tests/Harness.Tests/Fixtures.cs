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

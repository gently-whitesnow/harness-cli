namespace Harness.Tests;

public static class Fixtures
{
    public static RepositoryFixture Compliant() => Compliant(Frame.AllPresent());

    public static RepositoryFixture Framed()
        => RepositoryFixture.CreateGitRepository().WriteFile(".harness.json", Frame.AllPresent().ToString());

    public static RepositoryFixture Compliant(Frame frame)
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("AGENTS.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .WriteFile(".harness.json", frame.ToString())
            .Commit();

    public static RepositoryFixture WithoutAFrame()
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("AGENTS.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("CLAUDE.md", "AGENTS.md")
            .Commit();

    public static RepositoryFixture WithRawFrame(string frame)
        => WithoutAFrame().WriteFile(".harness.json", frame).Commit();

    public const string PreviouslyRecognizedIntegrationProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
          <ItemGroup>
            <PackageReference Include="Microsoft.NET.Test.Sdk" Version="17.14.1" />
            <PackageReference Include="Microsoft.AspNetCore.Mvc.Testing" Version="10.0.0" />
          </ItemGroup>
        </Project>

        """;

    public const string PreviouslyRecognizedWebManifest =
        """
        {
          "name": "web-fixture",
          "private": true,
          "version": "0.0.0",
          "scripts": {
            "test:e2e": "playwright test"
          },
          "devDependencies": {
            "@playwright/test": "^1.48.0",
            "dependency-cruiser": "^16.0.0"
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

    public const string HardenedBuildProps =
        """
        <Project>
          <PropertyGroup>
            <Nullable>enable</Nullable>
            <ImplicitUsings>enable</ImplicitUsings>
            <TreatWarningsAsErrors>true</TreatWarningsAsErrors>
            <EnableNETAnalyzers>true</EnableNETAnalyzers>
            <AnalysisLevel>latest-Recommended</AnalysisLevel>
            <EnforceCodeStyleInBuild>true</EnforceCodeStyleInBuild>
            <Deterministic>true</Deterministic>
            <ContinuousIntegrationBuild Condition="'$(CI)' == 'true'">true</ContinuousIntegrationBuild>
          </PropertyGroup>
        </Project>
        """;

    public const string SimpleSdkProject =
        """
        <Project Sdk="Microsoft.NET.Sdk">
          <PropertyGroup>
            <TargetFramework>net10.0</TargetFramework>
          </PropertyGroup>
        </Project>
        """;
}

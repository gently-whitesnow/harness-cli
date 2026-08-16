namespace Harness.Tests;

/// <summary>
/// Repository shapes shared by several acceptance tests. Nothing here is ever built,
/// restored or executed: the harness reads tracked evidence only, so a fixture needs to be
/// tracked and readable, not runnable.
/// </summary>
public static class Fixtures
{
    /// <summary>
    /// A repository that satisfies the documentation policy and carries a frame that answers
    /// every question with a deliberate "no". Tests that are about one question override
    /// that one answer and leave the rest alone.
    /// </summary>
    public static RepositoryFixture Compliant() => Compliant(Frame.Answering());

    /// <summary>
    /// An empty repository that already carries a frame, so a test about something other
    /// than the frame can vary the rest freely without every run ending as incomplete.
    /// </summary>
    public static RepositoryFixture Framed()
        => RepositoryFixture.CreateGitRepository().WriteFile(".harness.json", Frame.Answering().ToString());

    public static RepositoryFixture Compliant(Frame frame)
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("ROOT.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("AGENTS.md", "ROOT.md")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .WriteFile(".harness.json", frame.ToString())
            .Commit();

    /// <summary>A repository with no frame at all: the state every new repository starts in.</summary>
    public static RepositoryFixture WithoutAFrame()
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("ROOT.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("AGENTS.md", "ROOT.md")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .Commit();

    /// <summary>A repository whose frame is exactly the given text, valid or not.</summary>
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
}

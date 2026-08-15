namespace Harness.Tests;

/// <summary>Repository shapes shared by several acceptance tests.</summary>
public static class Fixtures
{
    /// <summary>A repository that satisfies the documentation policy in full.</summary>
    public static RepositoryFixture Compliant()
        => RepositoryFixture.CreateGitRepository()
            .WriteFile("ROOT.md", "# Root\n\nNavigation.\n")
            .WriteFile("README.md", "# Overview\n")
            .WriteSymbolicLink("AGENTS.md", "ROOT.md")
            .WriteSymbolicLink("CLAUDE.md", "ROOT.md")
            .Commit();
}

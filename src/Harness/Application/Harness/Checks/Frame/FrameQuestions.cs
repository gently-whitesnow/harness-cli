namespace Harness.Checks.Frame;

/// <summary>The frame's questions in the order the report asks them.</summary>
internal static class FrameQuestions
{
    public static readonly IReadOnlyList<FrameQuestion> All =
    [
        new("tests.unit", "unit tests", "repository answer about unit tests", FrameExplanations.UnitTests)
        {
            AddressesTestProjects = true,
        },
        new(
            "tests.integration",
            "integration tests",
            "repository answer about integration tests",
            FrameExplanations.IntegrationTests)
        {
            AddressesTestProjects = true,
        },
        new(
            "tests.architecture",
            "architecture rules",
            "repository answer about architecture rules",
            FrameExplanations.Architecture),
        new("format", "source formatting", "repository answer about source formatting", FrameExplanations.Format),
        new("lint", "static analysis", "repository answer about static analysis", FrameExplanations.Lint),
        new("build", "the build entry point", "repository answer about its build entry point", FrameExplanations.Build),
        new(
            "typecheck",
            "ahead-of-time type checking",
            "repository answer about type checking",
            FrameExplanations.Typecheck),
        new(
            "verify",
            "the repository verification entry point",
            "repository answer about its unified verification script",
            FrameExplanations.Verify)
        {
            RequiresLocation = true,
            AppliesToEveryRepository = true,
        },
    ];
}

using Harness.Checks.Cohesion;
using Harness.Checks.Comments;
using Harness.Checks.Commits;
using Harness.Checks.Dependencies;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Frame;
using Harness.Checks.Maintainability;
using Harness.Checks.TypesPerFile;
using Harness.Config;
using Harness.Languages.CSharp;

namespace Harness.Checks;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
/// <remarks>
/// The frame is read first, because every frame question reads it. Then the analyses the
/// harness performs itself, which drift between repositories and which no repository's own
/// pipeline measures the same way. Then the frame's questions, the same for every repository.
/// </remarks>
internal static class CheckRegistry
{
    public static readonly IReadOnlyList<IRepositoryCheck> All =
    [
        new HarnessConfigCheck(),

        new DocumentationPolicyCheck(),
        new CommitSetupCheck(),
        new CommentLineCheck(),
        new TypesPerFileCheck(),
        new DependenciesCheck(new CSharpAnalyzer()),
        new CohesionCheck(new CSharpAnalyzer()),
        new MaintainabilityCheck(),
        new DuplicationCheck(),

        new BuildPropertiesCheck(),
        new CentralPackagesCheck(),
        new SolutionFormatCheck(),

        new UnitTestFrameCheck(),
        new IntegrationTestFrameCheck(),
        new ArchitectureFrameCheck(),
        new FormatFrameCheck(),
        new LintFrameCheck(),
        new BuildFrameCheck(),
        new TypecheckFrameCheck(),
    ];

    /// <summary>How the frame sees the shipped checks, so it never has to reach for one.</summary>
    public static IReadOnlyList<CheckDescriptor> Describe(IReadOnlyList<IRepositoryCheck> checks)
        => checks
            .Select(check => new CheckDescriptor(
                check.Id,
                check.Group,
                check.Applicability,
                (check as FrameQuestionCheck)?.AnswerKey,
                (check as FrameQuestionCheck)?.IntroducedIn ?? 0))
            .ToList();
}

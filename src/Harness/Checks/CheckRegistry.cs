using Harness.Checks.Comments;
using Harness.Checks.Commits;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Frame;
using Harness.Checks.Maintainability;
using Harness.Checks.TypesPerFile;
using Harness.Config;

namespace Harness.Checks;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
/// <remarks>
/// The frame is read first, because every frame question reads it. Then the two analyses
/// the harness performs itself — the things that drift between repositories and that no
/// repository's own pipeline measures the same way. Then the frame's questions, which are
/// the same for every repository this tool is pointed at.
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
}

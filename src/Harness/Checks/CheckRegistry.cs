using Harness.Checks.Declarations;
using Harness.Checks.Duplication;
using Harness.Checks.Maintainability;
using Harness.Config;

namespace Harness.Checks;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
/// <remarks>
/// The frame is read first, because every declaration check reads it. Then the two analyses
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
        new MaintainabilityCheck(),
        new DuplicationCheck(),

        new UnitTestDeclarationCheck(),
        new IntegrationTestDeclarationCheck(),
        new ArchitectureDeclarationCheck(),
        new FormatDeclarationCheck(),
        new LintDeclarationCheck(),
        new BuildDeclarationCheck(),
        new TypecheckDeclarationCheck(),
    ];
}

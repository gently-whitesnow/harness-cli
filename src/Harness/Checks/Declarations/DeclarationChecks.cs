using Harness.Checks.Surfaces;

namespace Harness.Checks.Declarations;

/// <summary>
/// Whether the repository owns tests that exercise its units in isolation. Git cannot tell
/// a unit test from an integration test — both carry the same SDK marker — so the address
/// in the frame is the only thing that separates them, and it is the repository that draws
/// the line.
/// </summary>
internal sealed class UnitTestDeclarationCheck : DeclarationCheck
{
    protected override string Key => "tests.unit";

    protected override string Subject => "unit tests";

    protected override string AddressExample => "tests/Unit";

    public override string Summary => "declared unit tests, proven by address";

    public override string Explanation => DeclarationExplanations.UnitTests;

    protected override string LookedFor =>
        $"{RecognizedEvidence.List(DotNetSurface.TestProjectMarkers)} in tracked projects, "
            + $"and the scripts {RecognizedEvidence.List(RecognizedEvidence.UnitTestScripts)}";

    /// <summary>
    /// A test SDK marker or a `test` script appears in every repository that tests anything
    /// at all, including one whose tests are deliberately not unit tests. It is a useful
    /// hint about where the address might be and a dishonest basis for calling an answer
    /// wrong, so here evidence informs and never refutes.
    /// </summary>
    protected override bool EvidenceRefutes => false;

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => context.DotNet
            .ProjectsCarrying(context.Repository, DotNetSurface.TestProjectMarkers)
            .Select(carrier => new EvidenceItem(carrier.Path, $"declares {carrier.Name}"))
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.UnitTestScripts))
            .ToList();
}

/// <summary>
/// Whether the repository owns tests that exercise components together rather than in
/// isolation. What Git can refute is a library whose purpose is running the real thing: an
/// in-process host, a container, a browser driver.
/// </summary>
internal sealed class IntegrationTestDeclarationCheck : DeclarationCheck
{
    protected override string Key => "tests.integration";

    protected override string Subject => "integration tests";

    protected override string AddressExample => "tests/Integration";

    public override string Summary => "declared integration tests, proven by address";

    public override string Explanation => DeclarationExplanations.IntegrationTests;

    protected override string LookedFor =>
        $"{RecognizedEvidence.List(RecognizedEvidence.IntegrationPackages)} in tracked projects, the scripts "
            + $"{RecognizedEvidence.List(RecognizedEvidence.IntegrationScripts)}, and dependencies on "
            + $"{RecognizedEvidence.List(RecognizedEvidence.IntegrationDependencies)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => context.DotNet
            .ProjectsCarrying(context.Repository, RecognizedEvidence.IntegrationPackages)
            .Select(carrier => new EvidenceItem(carrier.Path, $"depends on {carrier.Name}"))
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.IntegrationScripts))
            .Concat(RecognizedEvidence.Dependencies(context.Web, RecognizedEvidence.IntegrationDependencies))
            .ToList();
}

/// <summary>
/// Whether the repository asserts its own architectural rules executably. A test project is
/// not evidence of this: tests prove behaviour, and only a library built to assert structure
/// shows that structure is being asserted.
/// </summary>
internal sealed class ArchitectureDeclarationCheck : DeclarationCheck
{
    protected override string Key => "tests.architecture";

    protected override string Subject => "architecture rules";

    protected override string AddressExample => "tests/Architecture";

    public override string Summary => "declared architecture rules, proven by address";

    public override string Explanation => DeclarationExplanations.Architecture;

    protected override string LookedFor =>
        $"{RecognizedEvidence.List(RecognizedEvidence.ArchitecturePackages)} in tracked projects, and dependencies "
            + $"on {RecognizedEvidence.List(RecognizedEvidence.ArchitectureDependencies)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => context.DotNet
            .ProjectsCarrying(context.Repository, RecognizedEvidence.ArchitecturePackages)
            .Select(carrier => new EvidenceItem(carrier.Path, $"depends on {carrier.Name}"))
            .Concat(RecognizedEvidence.Dependencies(context.Web, RecognizedEvidence.ArchitectureDependencies))
            .ToList();
}

/// <summary>Whether the repository pins how its source is formatted.</summary>
internal sealed class FormatDeclarationCheck : DeclarationCheck
{
    protected override string Key => "format";

    protected override string Subject => "a pinned source format";

    protected override string AddressExample => ".editorconfig";

    public override string Summary => "declared formatting rules, proven by address";

    public override string Explanation => DeclarationExplanations.Format;

    protected override string LookedFor =>
        $"tracked {RecognizedEvidence.List(RecognizedEvidence.FormatFiles)}, and the scripts "
            + $"{RecognizedEvidence.List(RecognizedEvidence.FormatScripts)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => RecognizedEvidence.Files(context.Repository, RecognizedEvidence.FormatFiles)
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.FormatScripts))
            .ToList();
}

/// <summary>Whether the repository has rules about its source beyond how it is laid out.</summary>
internal sealed class LintDeclarationCheck : DeclarationCheck
{
    protected override string Key => "lint";

    protected override string Subject => "static analysis rules";

    protected override string AddressExample => ".globalconfig";

    public override string Summary => "declared static analysis, proven by address";

    public override string Explanation => DeclarationExplanations.Lint;

    protected override string LookedFor =>
        $"tracked {RecognizedEvidence.List(RecognizedEvidence.LintFiles)}, and the scripts "
            + $"{RecognizedEvidence.List(RecognizedEvidence.LintScripts)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => RecognizedEvidence.Files(context.Repository, RecognizedEvidence.LintFiles)
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.LintScripts))
            .ToList();
}

/// <summary>Whether the repository states what building it means.</summary>
internal sealed class BuildDeclarationCheck : DeclarationCheck
{
    protected override string Key => "build";

    protected override string Subject => "a declared build";

    protected override string AddressExample => "Repository.sln";

    public override string Summary => "declared build entry point, proven by address";

    public override string Explanation => DeclarationExplanations.Build;

    protected override string LookedFor =>
        $"tracked solutions and projects, and the scripts {RecognizedEvidence.List(RecognizedEvidence.BuildScripts)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => context.DotNet.Solutions
            .Select(path => new EvidenceItem(path, "is a tracked solution"))
            .Concat(context.DotNet.Projects.Select(entry => new EvidenceItem(entry.Path, "is a tracked project")))
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.BuildScripts))
            .ToList();
}

/// <summary>Whether the repository checks its own types ahead of running.</summary>
internal sealed class TypecheckDeclarationCheck : DeclarationCheck
{
    protected override string Key => "typecheck";

    protected override string Subject => "an ahead-of-time type check";

    protected override string AddressExample => "tsconfig.json";

    public override string Summary => "declared type checking, proven by address";

    public override string Explanation => DeclarationExplanations.Typecheck;

    protected override string LookedFor =>
        $"tracked {RecognizedEvidence.List(RecognizedEvidence.TypecheckFiles)}, and the scripts "
            + $"{RecognizedEvidence.List(RecognizedEvidence.TypecheckScripts)}";

    protected override IReadOnlyList<EvidenceItem> Evidence(CheckContext context)
        => RecognizedEvidence.Files(context.Repository, RecognizedEvidence.TypecheckFiles)
            .Concat(RecognizedEvidence.Scripts(context.Web, RecognizedEvidence.TypecheckScripts))
            .ToList();
}

using Harness.Checks.DotNet;
using Harness.Checks.Web;

namespace Harness.Checks.Capabilities;

/// <summary>
/// Whether the repository owns automated tests at all. The evidence is the evidence the test
/// gates already plan from: a project discovery classified as a test project, or a manifest
/// that declares the test script `web.test` would run.
/// </summary>
internal sealed class TestCapabilityCheck : CapabilityCheck
{
    public override string Id => "capability.tests";

    public override string Summary => "repository-owned automated tests";

    public override string Explanation => CapabilityExplanations.Tests;

    protected override string Capability => "automated tests";

    /// <summary>Test projects are recognized by discovery, not by a package this check names.</summary>
    protected override IReadOnlyList<string> DotNetPackages => [];

    protected override IReadOnlyList<string> DotNetLookedFor => DotNetSurface.TestProjectMarkers;

    protected override IReadOnlyList<string> WebScripts => WebScriptNames.Test;

    protected override IReadOnlyList<string> WebPackages => [];

    /// <summary>
    /// The scripts this check accepts are exactly the ones `web.test` chooses between, and it
    /// chooses the first that exists — the same one detected here.
    /// </summary>
    protected override bool WebGateRunsTheEvidence => true;

    protected override IReadOnlyList<CapabilityCarrier> CarriedBy(string projectPath, string projectText)
        => DotNetSurface.TestProjectMarkers.Any(marker =>
            projectText.Contains(marker, StringComparison.OrdinalIgnoreCase))
            ? [new CapabilityCarrier(projectPath, "a declared .NET test framework")]
            : [];
}

/// <summary>
/// Whether the repository owns tests that exercise components together rather than in
/// isolation. The recognized evidence is a library whose purpose is running the real thing:
/// an in-process host, a container, a browser driver.
/// </summary>
internal sealed class IntegrationCapabilityCheck : CapabilityCheck
{
    public override string Id => "capability.integration";

    public override string Summary => "repository-owned integration tests";

    public override string Explanation => CapabilityExplanations.Integration;

    protected override string Capability => "integration tests";

    protected override IReadOnlyList<string> DotNetPackages => RecognizedEvidence.IntegrationPackages;

    protected override IReadOnlyList<string> WebScripts => RecognizedEvidence.IntegrationScripts;

    protected override IReadOnlyList<string> WebPackages => RecognizedEvidence.IntegrationDependencies;
}

/// <summary>
/// Whether the repository asserts its own architectural rules executably. A test project is
/// not evidence of this: tests prove behaviour, and only a library built to assert structure
/// shows that structure is being asserted.
/// </summary>
internal sealed class ArchitectureCapabilityCheck : CapabilityCheck
{
    public override string Id => "capability.architecture";

    public override string Summary => "repository-owned architecture rules";

    public override string Explanation => CapabilityExplanations.Architecture;

    protected override string Capability => "architecture rules";

    protected override IReadOnlyList<string> DotNetPackages => RecognizedEvidence.ArchitecturePackages;

    protected override IReadOnlyList<string> WebScripts => [];

    protected override IReadOnlyList<string> WebPackages => RecognizedEvidence.ArchitectureDependencies;
}

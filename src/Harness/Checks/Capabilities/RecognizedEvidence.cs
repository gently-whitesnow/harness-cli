namespace Harness.Checks.Capabilities;

/// <summary>
/// Everything this version of the harness recognizes as evidence of a capability. The lists
/// live here because two readers need the same answer: the check that looks for them, and
/// `explain`, which has to state what was actually looked for. A report that names a
/// different list than the one that ran is worse than no report.
/// </summary>
internal static class RecognizedEvidence
{
    /// <summary>Libraries whose purpose is running the real thing rather than a stand-in.</summary>
    public static readonly string[] IntegrationPackages =
    [
        "Microsoft.AspNetCore.Mvc.Testing",
        "Microsoft.AspNetCore.TestHost",
        "Testcontainers",
        "DotNet.Testcontainers",
        "WireMock.Net",
        "Respawn",
    ];

    public static readonly string[] IntegrationScripts = ["test:integration", "test:e2e", "e2e"];

    public static readonly string[] IntegrationDependencies = ["@playwright/test", "cypress", "supertest"];

    /// <summary>Libraries built to assert structure rather than behaviour.</summary>
    public static readonly string[] ArchitecturePackages = ["NetArchTest", "ArchUnitNET"];

    public static readonly string[] ArchitectureDependencies = ["dependency-cruiser", "eslint-plugin-boundaries"];

    /// <summary>The list as `explain` prints it, so prose and behaviour cannot drift apart.</summary>
    public static string List(IReadOnlyList<string> names) => string.Join(", ", names);
}

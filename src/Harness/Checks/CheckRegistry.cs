using Harness.Checks.Capabilities;
using Harness.Checks.DotNet;
using Harness.Checks.Duplication;
using Harness.Checks.Maintainability;
using Harness.Checks.Web;

namespace Harness.Checks;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
internal static class CheckRegistry
{
    public static readonly IReadOnlyList<IRepositoryCheck> All =
    [
        new DocumentationPolicyCheck(),
        new MaintainabilityCheck(),
        new DuplicationCheck(),
        new DotNetFormatCheck(),
        new DotNetBuildCheck(),
        new DotNetTestCheck(),
        new WebFormatCheck(),
        new WebLintCheck(),
        new WebTypecheckCheck(),
        new WebTestCheck(),
        new WebBuildCheck(),

        // The capability readers run last: what they may say about a repository depends on
        // which gates already ran over the same evidence in this run.
        new TestCapabilityCheck(),
        new IntegrationCapabilityCheck(),
        new ArchitectureCapabilityCheck(),
    ];
}

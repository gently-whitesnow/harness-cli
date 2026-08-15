namespace Harness.Checks;

/// <summary>The checks this version of the harness ships, in execution order.</summary>
internal static class CheckRegistry
{
    public static readonly IReadOnlyList<IRepositoryCheck> All =
    [
        new DocumentationPolicyCheck(),
    ];
}

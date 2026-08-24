namespace Harness.Config;

internal sealed record DependencySettings(
    int ExternalImports,
    int OutgoingReferences,
    int IncomingReferences)
{
    public static DependencySettings Default { get; } = new(
        ExternalImports: 20,
        OutgoingReferences: 15,
        IncomingReferences: 20);
}

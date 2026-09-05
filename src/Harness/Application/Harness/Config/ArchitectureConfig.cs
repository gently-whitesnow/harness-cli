namespace Harness.Config;

internal sealed record ArchitectureConfig(string? Standard, string? NotApplicableReason)
{
    public bool IsApplicable => Standard is not null;
}

namespace Harness.Config;

internal sealed record MaintainabilitySettings(
    int FileLines,
    int TypeLines,
    int MethodLines,
    int ConstructorParameters,
    int PublicMembers)
{
    public static MaintainabilitySettings Default { get; } = new(
        FileLines: 400,
        TypeLines: 300,
        MethodLines: 60,
        ConstructorParameters: 6,
        PublicMembers: 25);
}

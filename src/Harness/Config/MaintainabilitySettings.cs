namespace Harness.Config;

internal sealed record MaintainabilitySettings(
    int FileLines,
    int TypeLines,
    int MethodLines,
    int Branches,
    int ConstructorParameters,
    int PublicMembers)
{
    public static MaintainabilitySettings Default { get; } = new(
        FileLines: 400,
        TypeLines: 300,
        MethodLines: 60,
        Branches: 12,
        ConstructorParameters: 6,
        PublicMembers: 25);

    public MaintainabilitySettings With(string name, int value) => name switch
    {
        "fileLines" => this with { FileLines = value },
        "typeLines" => this with { TypeLines = value },
        "methodLines" => this with { MethodLines = value },
        "branches" => this with { Branches = value },
        "constructorParameters" => this with { ConstructorParameters = value },
        "publicMembers" => this with { PublicMembers = value },
        _ => this,
    };
}

namespace Harness.Config;

internal sealed record CohesionSettings(int MinimumMembers, int Groups)
{
    public static CohesionSettings Default { get; } = new(MinimumMembers: 6, Groups: 1);

    public CohesionSettings With(string name, int value) => name switch
    {
        "minimumMembers" => this with { MinimumMembers = value },
        "groups" => this with { Groups = value },
        _ => this,
    };
}

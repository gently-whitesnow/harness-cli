namespace Harness.Structure;

internal sealed record TypeCohesion(
    string Subject,
    string Path,
    int Line,
    IReadOnlyList<CohesionMember> Members)
{
    public string Location => $"{Path}:{Line}";

    public int StateMembers => Members.Count(member => member.IsState);
}

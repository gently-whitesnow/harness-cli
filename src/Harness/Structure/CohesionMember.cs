namespace Harness.Structure;

/// <summary>
/// One member of a type: either state it holds, or behaviour that mentions other members by
/// name. The mentions are what connects members into groups.
/// </summary>
internal sealed record CohesionMember(string Name, bool IsState, IReadOnlyList<string> Mentions);

namespace Harness.Checks.Complexity;

/// <summary>
/// The DSM ceiling of sliced-dotnet/1 (ADR-0052): mean reach is the reach inside a slice plus
/// the files that see the whole product, and neither grows with the product, so the limit is a
/// property of the standard and lives in the binary rather than in a tracked file.
/// </summary>
internal static class ComplexityLimit
{
    /// <summary>Files a change reaches on average, itself included, at most.</summary>
    public const double MeanReach = 8.0;

    /// <summary>Files in the largest cyclic group, at most: the product graph is a DAG.</summary>
    public const int CoreSize = 0;

    public const int NamedHubs = 5;
}

namespace Harness.Config;

/// <summary>
/// The DSM ceiling a repository declares: files a change reaches on average, itself included,
/// and files in the largest cyclic group. The contract defaults describe sliced-dotnet/1, where
/// reach is bounded by one slice plus the composition root and the product graph is a DAG.
/// </summary>
internal sealed record ComplexitySettings(double MeanReach, int CoreSize)
{
    public static ComplexitySettings Default { get; } = new(MeanReach: 8.0, CoreSize: 0);
}

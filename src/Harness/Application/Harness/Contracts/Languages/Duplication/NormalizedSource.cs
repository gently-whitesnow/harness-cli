namespace Harness.Languages.Duplication;

/// <summary>One tracked file reduced to the normalized lines the duplication rule compares.</summary>
internal sealed record NormalizedSource(string Path, IReadOnlyList<NormalizedLine> Lines);

using Harness.Languages;

namespace Harness.Config;

/// <summary>
/// Every comparison point the frame declares, keyed by the check that reads it. A check that is
/// not in the frame has no section here, and a check in the frame always finds its own.
/// </summary>
internal sealed record HarnessSettings(
    IReadOnlyDictionary<string, DuplicationSettings> Duplication,
    IReadOnlyDictionary<string, ComplexitySettings> Complexity,
    IReadOnlyDictionary<string, FunctionSettings> Functions,
    AdrShapeSettings? AdrShape,
    VerifyEntrypointSettings? VerifyEntrypoint,
    CommitSettings Commits,
    IReadOnlyDictionary<string, IReadOnlyDictionary<string, string>> RepositoryWide)
{
    public const string DuplicationGroup = "duplication";

    public const string ComplexityGroup = "complexity";
    public const string FunctionsGroup = "functions";
    public const string AdrShapeGroup = "adrs";
    public const string VerifyGroup = "verify";

    public const string CommitsSection = "commits";

    /// <summary>The families whose checks read a settings section named by their check id.</summary>
    public static readonly IReadOnlyList<string> ConfigurableGroups =
        [DuplicationGroup, ComplexityGroup, FunctionsGroup, AdrShapeGroup, VerifyGroup];

    public static bool HasSection(string group)
        => ConfigurableGroups.Contains(group, StringComparer.Ordinal);

    public DuplicationSettings? DuplicationFor(Language language)
        => Duplication.TryGetValue(language.Qualify(DuplicationGroup), out var settings) ? settings : null;

    public ComplexitySettings? ComplexityFor(Language language)
        => Complexity.TryGetValue(language.Qualify(ComplexityGroup), out var settings) ? settings : null;

    public FunctionSettings? FunctionsFor(Language language)
        => Functions.TryGetValue(language.Qualify(FunctionsGroup), out var settings) ? settings : null;

    public IReadOnlyDictionary<string, string> RepositoryWideFor(string checkId)
        => RepositoryWide.TryGetValue(checkId, out var entries) ? entries : new Dictionary<string, string>();
}

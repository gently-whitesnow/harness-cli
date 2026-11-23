using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// The repository's harness frame: applicability, explicit settings and check policy.
/// </summary>
internal sealed record HarnessConfig
{
    public const string FileName = ".harness.json";

    public const string RetiredBudgetFileName = ".harness.budget.json";

    public IReadOnlyList<string> Projects { get; init; } = [];

    public IReadOnlyList<(IReadOnlyList<string> Paths, string Reason)> Generated { get; init; } = [];

    public required HarnessVersion Version { get; init; }

    public required bool TracksLatest { get; init; }

    public required ArchitectureConfig? Architecture { get; init; }

    public required string? ArchitectureFailure { get; init; }

    public required IReadOnlyDictionary<string, ApplicabilityAnswer> Applicability { get; init; }

    public required HarnessSettings Settings { get; init; }

    public required IReadOnlyDictionary<string, CheckPolicy> Policy { get; init; }

    public bool TryPolicyFor(string checkId, out CheckPolicy policy)
        => Policy.TryGetValue(checkId, out policy);

    public ApplicabilityAnswer? NotApplicable(string? key)
        => key is not null
            && Applicability.TryGetValue(key, out var answer)
            && !answer.IsApplicable
                ? answer
                : null;

    /// <summary>
    /// The smallest config for a C# application, shown whenever there is none. A reader who has
    /// never seen this file should not have to find documentation to start; `harness init`
    /// writes the same shape for the stacks the index actually shows.
    /// </summary>
    public static string Template =>
        $$"""
        A minimal .harness.json for a C# application, committed at the repository root
        (`harness init` writes one for the languages the index shows):

          {
            "version": "{{HarnessVersion.Current}}",
            "architecture": { "standard": "sliced-dotnet/1" },
            "applicability": {
              "csharp": { "applicable": true },
              "dotnet": { "applicable": true }
            },
            "settings": {
              "complexity.csharp": {
                "averageReachableFiles": 8.0,
                "largestCyclicGroupSize": 0
              },
              "adrs.shape": {
                "fencedBlockLineLimit": 20
              },
              "duplication.csharp": {
                "windowLines": 30,
                "minimumTokens": 90
              },
              "functions.csharp": {
                "ownTokens": 700
              },
              "verify.entrypoint": {
                "path": "verify.sh"
              },
              "warning-suppressions.dotnet": {
                "repositoryWide": [
                  { "id": "CA1707", "reason": "sentence-style test names use underscores" }
                ]
              },
              "commits": {
                "language": "ru",
                "requireSetup": true
              }
            },
            "policy": {
              "harness.config": "required",
              "harness.coverage": "required",
              "architecture.sliced-dotnet.zone-shape": "required",
              "architecture.sliced-dotnet.slice-shape": "required",
              "architecture.sliced-dotnet.segment-names": "required",
              "architecture.sliced-dotnet.layer-assemblies": "required",
              "architecture.sliced-dotnet.dependency-direction": "required",
              "architecture.sliced-dotnet.slice-isolation": "required",
              "architecture.sliced-dotnet.public-api": "required",
              "architecture.sliced-dotnet.cross-api": "required",
              "complexity.csharp": "required",
              "docs.policy": "required",
              "adrs.shape": "required",
              "commits.setup": "required",
              "types-per-file.csharp": "required",
              "dependencies.csharp": "required",
              "duplication.csharp": "required",
              "functions.csharp": "required",
              "build-properties.dotnet": "required",
              "central-packages.dotnet": "required",
              "solution-format.dotnet": "required",
              "editorconfig.dotnet": "required",
              "warning-suppressions.dotnet": "required",
              "verify.entrypoint": "required"
            }
          }

        A check the policy does not name is outside the frame and does not run; a check it names
        carries required, advisory or off, and its settings section in full. An axis with tracked
        sources must be declared applicable or declined with a reason — harness.coverage says which.
        Generated tracked sources need a top-level `generated` array of paths and reasons; only
        files under those paths with a toolchain marker are excluded. Repository-wide diagnostic
        switches need id/reason entries in settings.<check>.repositoryWide; the CA1707 entry pairs
        with the reference .editorconfig `init` writes, and goes when that switch goes.
        Run `harness explain <check-id>` for what one check proves and how to fix it.
        """;

    /// <summary>The smallest frame for a registered project; pin and commit settings stay in the root.</summary>
    public static string ProjectTemplate =>
        $$"""
        A minimal {{FileName}} for a registered project, committed in the project directory
        (`version`, `projects`, `settings.commits` and `policy.commits.setup` stay in the root):

          {
            "applicability": {},
            "settings": {
              "adrs.shape": {
                "fencedBlockLineLimit": 20
              }
            },
            "policy": {
              "harness.coverage": "required",
              "docs.policy": "required",
              "adrs.shape": "required"
            }
          }

        Applicability, settings and policy are not inherited from the root. Run
        `harness explain harness.config` for the workspace rules.
        """;
}

using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// The repository's own answers to the harness frame and how strictly each check is treated.
/// Answers are self-reported: validated for form, never inspected or fact-checked.
/// </summary>
internal sealed record HarnessConfig
{
    public const string FileName = ".harness.json";

    public const string FrameGroup = "frame";

    public const string RetiredBudgetFileName = ".harness.budget.json";

    public IReadOnlyList<string> Projects { get; init; } = [];

    public required HarnessVersion Version { get; init; }

    public required bool TracksLatest { get; init; }

    public required ArchitectureConfig? Architecture { get; init; }

    public required string? ArchitectureFailure { get; init; }

    public required IReadOnlyDictionary<string, FrameAnswer> Answers { get; init; }

    public required IReadOnlyDictionary<string, string> AnswerFailures { get; init; }

    public required IReadOnlyDictionary<string, ApplicabilityAnswer> Applicability { get; init; }

    public required HarnessSettings Settings { get; init; }

    public required IReadOnlyDictionary<string, CheckPolicy> Policy { get; init; }

    public FrameAnswer? Answered(string key)
        => Answers.TryGetValue(key, out var answer) ? answer : null;

    public string? AnswerFailure(string key)
        => AnswerFailures.TryGetValue(key, out var failure) ? failure : null;

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
            "answers": {
              "tests.unit": { "paths": ["tests/Unit"] },
              "tests.integration": { "present": false, "reason": "no external dependencies yet" },
              "tests.architecture": { "present": false, "reason": "planned" },
              "format": { "paths": [".editorconfig"] },
              "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
              "build": { "paths": ["Repository.slnx"] },
              "typecheck": { "applicable": false, "reason": "no web stack" },
              "verify": { "paths": ["verify.sh"] }
            },
            "applicability": {
              "csharp": { "applicable": true },
              "dotnet": { "applicable": true }
            },
            "settings": {
              "comments.csharp": {
                "minimumCommentLines": 10,
                "percentageLimit": 8
              },
              "duplication.csharp": {
                "windowLines": 30,
                "minimumTokens": 90
              },
              "complexity.csharp": {
                "averageReachableFiles": 8.0,
                "largestCyclicGroupSize": 0
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
              "commits.setup": "required",
              "comments.csharp": "required",
              "types-per-file.csharp": "required",
              "dependencies.csharp": "required",
              "duplication.csharp": "required",
              "build-properties.dotnet": "required",
              "central-packages.dotnet": "required",
              "solution-format.dotnet": "required",
              "editorconfig.dotnet": "required",
              "warning-suppressions.dotnet": "required",
              "frame.tests.unit": "required",
              "frame.tests.integration": "required",
              "frame.tests.architecture": "required",
              "frame.format": "required",
              "frame.lint": "required",
              "frame.build": "required",
              "frame.typecheck": "required",
              "frame.verify": "required"
            }
          }

        A check the policy does not name is outside the frame and does not run; a check it names
        carries required, advisory or off, and its settings section in full. An axis with tracked
        sources must be declared applicable or declined with a reason — harness.coverage says which.
        Run `harness explain <check-id>` for what one answer means and how it is reported.
        """;
}

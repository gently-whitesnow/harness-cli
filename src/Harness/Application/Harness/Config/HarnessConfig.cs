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
    /// The smallest config that answers everything, shown whenever there is none. A reader
    /// who has never seen this file should not have to find documentation to start.
    /// </summary>
    public static string Template =>
        $$"""
        A minimal .harness.json, committed at the repository root:

          {
            "version": "{{HarnessVersion.Current}}",
            "architecture": { "standard": "sliced-dotnet/1" },
            "answers": {
              "tests.unit": { "paths": ["tests/Unit"] },
              "tests.integration": { "present": false, "reason": "no external dependencies yet" },
              "tests.architecture": { "present": false, "reason": "planned" },
              "format": { "paths": [".editorconfig"] },
              "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
              "build": { "paths": ["Repository.sln"] },
              "typecheck": { "applicable": false, "reason": "no web stack" },
              "verify": { "paths": ["verify.sh"] }
            },
            "applicability": {
              "csharp": { "applicable": true },
              "dotnet": { "applicable": true },
              "yaml": { "applicable": true },
              "typescript": { "applicable": true }
            },
            "settings": {
              "comments.csharp": {
                "minimumCommentLines": 10,
                "percentageLimit": 8
              },
              "comments.yaml": {
                "minimumCommentLines": 10,
                "percentageLimit": 8
              },
              "comments.typescript": {
                "minimumCommentLines": 10,
                "percentageLimit": 8
              },
              "duplication.csharp": {
                "windowLines": 30,
                "minimumTokens": 90
              },
              "commits": {
                "language": "ru",
                "requireSetup": true
              }
            },
            "policy": {
              "harness.config": "required",
              "architecture.sliced-dotnet": "required",
              "complexity.csharp": "required",
              "docs.policy": "required",
              "commits.setup": "required",
              "comments.csharp": "required",
              "comments.yaml": "required",
              "comments.typescript": "required",
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

        Run `harness explain <check-id>` for what one answer means and how it is reported.
        """;
}

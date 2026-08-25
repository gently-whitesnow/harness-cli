using System.Text.Json;
using Harness.Git;
using Harness.Versioning;

namespace Harness.Config;

internal enum FrameAnswerKind
{
    Located,
    Present,
    Absent,
    NotApplicable,
}

internal enum CheckPolicy
{
    Required,
    Advisory,
    Off,
}

/// <summary>
/// The repository's own answers to the harness frame, how strictly each check is treated,
/// and which findings it has consciously accepted and why.
/// </summary>
/// <remarks>
/// Answers are self-reported. The harness validates each question independently and reports
/// local answer problems on that question, but it neither inspects an address nor searches Git
/// for a contradiction. The frame is described to this reader as data — it never reaches for
/// the checks themselves, so reading a repository does not depend on being able to run one.
/// </remarks>
internal sealed record HarnessConfig
{
    public const string FileName = ".harness.json";

    public const string FrameGroup = "frame";

    private static readonly string[] TopLevelKeys =
        ["version", "answers", "applicability", "settings", "policy", "suppress"];

    public required HarnessVersion Version { get; init; }

    public required bool TracksLatest { get; init; }

    public required IReadOnlyDictionary<string, FrameAnswer> Answers { get; init; }

    public required IReadOnlyDictionary<string, string> AnswerFailures { get; init; }

    public required IReadOnlyDictionary<string, ApplicabilityAnswer> Applicability { get; init; }

    public required HarnessSettings Settings { get; init; }

    public required IReadOnlyDictionary<string, CheckPolicy> Policy { get; init; }

    public required IReadOnlyList<Suppression> Suppressions { get; init; }

    public FrameAnswer? Answered(string key)
        => Answers.TryGetValue(key, out var answer) ? answer : null;

    public string? AnswerFailure(string key)
        => AnswerFailures.TryGetValue(key, out var failure) ? failure : null;

    /// <summary>Whether the pinned release already shipped the thing introduced in <paramref name="since"/>.</summary>
    public bool Includes(HarnessVersion since)
        => TracksLatest || since <= Version;

    public CheckPolicy PolicyFor(string checkId, string group)
    {
        if (Policy.TryGetValue(checkId, out var byId))
        {
            return byId;
        }

        return Policy.TryGetValue(group, out var byGroup) ? byGroup : CheckPolicy.Required;
    }

    public ApplicabilityAnswer? NotApplicable(string? key)
        => key is not null && Applicability.TryGetValue(key, out var answer) ? answer : null;

    /// <summary>
    /// Reads the tracked config and validates its envelope before preserving per-answer results.
    /// An untracked config does not exist for the harness, the same as any untracked file: what
    /// verifies a repository has to be part of it. Every failure names what to fix rather than
    /// degrading to a default; answer failures stay local, while failures that make policy
    /// unreliable remain global.
    /// </summary>
    public static (HarnessConfig? Config, string? Failure) Load(
        GitRepository repository,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var entry = repository.TrackedEntries.FirstOrDefault(candidate => candidate.Path == FileName);
        if (entry is null)
        {
            return (null, $"'{FileName}' is not tracked in this repository, so nothing about the harness frame "
                + $"can be established.{Environment.NewLine}{Template}");
        }

        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure!);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException exception)
        {
            return (null, $"'{FileName}' is not readable as JSON ({exception.Message}).");
        }

        using (document)
        {
            return Read(document.RootElement, checks);
        }
    }

    private static (HarnessConfig? Config, string? Failure) Read(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("the document is not a JSON object"));
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!TopLevelKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, ConfigJson.Failure($"'{property.Name}' is not a key this harness reads "
                    + $"(expected {string.Join(", ", TopLevelKeys)})"));
            }
        }

        var (version, tracksLatest, versionFailure) = ReadVersion(root);
        return versionFailure is not null
            ? (null, ConfigJson.Failure(versionFailure))
            : Assemble(root, checks, version, tracksLatest);
    }

    private static (HarnessConfig? Config, string? Failure) Assemble(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks,
        HarnessVersion version,
        bool tracksLatest)
    {
        var (answers, answerFailures, answerFailure) = FrameAnswerReader.Read(
            root,
            checks.Where(check => check.AnswerKey is not null).ToList(),
            version,
            tracksLatest);
        if (answers is null)
        {
            return (null, answerFailure);
        }

        var (applicability, applicabilityFailure) = PolicyReader.ReadApplicability(root, checks);
        if (applicability is null)
        {
            return (null, applicabilityFailure);
        }

        var (settings, settingsFailure) = HarnessSettingsReader.Read(root, version);
        if (settings is null)
        {
            return (null, ConfigJson.Failure(settingsFailure!));
        }

        var selectors = Selectors(checks);
        var (policy, policyFailure) = PolicyReader.ReadPolicy(root, selectors);
        if (policy is null)
        {
            return (null, policyFailure);
        }

        var (suppressions, suppressionFailure) = PolicyReader.ReadSuppressions(root, selectors);
        return suppressions is null
            ? (null, suppressionFailure)
            : (new HarnessConfig
            {
                Version = version,
                TracksLatest = tracksLatest,
                Answers = answers,
                AnswerFailures = answerFailures!,
                Applicability = applicability,
                Settings = settings,
                Policy = policy,
                Suppressions = suppressions,
            }, null);
    }

    /// <summary>
    /// The pinned release is the whole contract: which questions are asked, which checks run
    /// and which defaults they use. A binary may be newer than the pin and then reproduces the
    /// pinned release; it may never be older, because it cannot know what it does not ship.
    /// </summary>
    private static (HarnessVersion Version, bool TracksLatest, string? Failure) ReadVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out var declared) || declared.ValueKind != JsonValueKind.String)
        {
            return (default, false, Expected);
        }

        var text = declared.GetString();
        if (string.Equals(text, "latest", StringComparison.Ordinal))
        {
            return (HarnessVersion.Current, true, null);
        }

        if (!HarnessVersion.TryParse(text, out var version))
        {
            return (default, false, Expected);
        }

        if (version > HarnessVersion.Current)
        {
            return (default, false, $"'version' pins harness {version}, which is newer than this binary "
                + $"({HarnessVersion.Current}); update the harness before checking this repository");
        }

        return version < HarnessVersion.Minimum
            ? (default, false, $"'version' pins harness {version}, which this binary no longer reproduces "
                + $"(the oldest it knows is {HarnessVersion.Minimum}); raise the pin with `harness upgrade`")
            : (version, false, null);
    }

    private static string Expected
        => $"'version' must be a harness release such as \"{HarnessVersion.Current}\", or \"latest\"";

    private static List<string> Selectors(IReadOnlyList<CheckDescriptor> checks)
        => checks.Select(check => check.Id)
            .Concat(checks.Select(check => check.Group))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    /// <summary>
    /// The smallest config that answers everything, shown whenever there is none. A reader
    /// who has never seen this file should not have to find documentation to start.
    /// </summary>
    public static string Template =>
        """
        A minimal .harness.json, committed at the repository root:

          {
            "version": "1.0.0",
            "answers": {
              "tests.unit": { "paths": ["tests/Unit"] },
              "tests.integration": { "present": false, "reason": "no external dependencies yet" },
              "tests.architecture": { "present": false, "reason": "planned" },
              "format": { "paths": [".editorconfig"] },
              "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
              "build": { "paths": ["Repository.sln"] },
              "typecheck": { "applicable": false, "reason": "no web stack" }
            },
            "applicability": {},
            "settings": {
              "comments.csharp": {
                "minimumCommentLines": 10,
                "percentageLimit": 8
              },
              "maintainability.csharp": {
                "fileLines": 400,
                "typeLines": 300,
                "methodLines": 60,
                "branches": 12,
                "constructorParameters": 6,
                "publicMembers": 25
              },
              "dependencies.csharp": {
                "externalImports": 20,
                "outgoingReferences": 15,
                "incomingReferences": 20
              },
              "cohesion.csharp": {
                "minimumMembers": 6,
                "groups": 2
              },
              "duplication.csharp": {
                "windowLines": 30,
                "minimumTokens": 90
              },
              "commits": {
                "language": "en",
                "requireSetup": true
              }
            }
          }

        Run `harness explain <check-id>` for what one answer means and how it is reported.
        """;
}

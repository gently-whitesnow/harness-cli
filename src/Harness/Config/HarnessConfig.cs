using System.Text.Json;
using Harness.Checks;
using Harness.Checks.Frame;
using Harness.Git;

namespace Harness.Config;

internal enum FrameAnswerKind
{
    Located,
    Present,
    Absent,
    NotApplicable,
}

internal sealed record FrameAnswer(
    string Key,
    FrameAnswerKind Kind,
    IReadOnlyList<string> Paths,
    string? Reason);

internal enum CheckPolicy
{
    Default,
    Required,
    Advisory,
    Off,
}

internal sealed record Suppression(string Check, string Location, string Reason);

/// <summary>
/// The repository's own answers to the harness frame, how strictly each check is treated,
/// and which findings it has consciously accepted and why.
/// </summary>
/// <remarks>
/// Answers are self-reported. The harness validates each question independently and reports
/// local answer problems on that question, but it neither inspects an address nor searches Git
/// for a contradiction.
/// </remarks>
internal sealed class HarnessConfig
{
    public const string FileName = ".harness.json";

    private static readonly string[] TopLevelKeys = ["version", "answers", "settings", "policy", "suppress"];

    public const int CurrentVersion = 2;

    private const int MinimumVersion = 2;

    private HarnessConfig(
        int version,
        bool tracksLatest,
        IReadOnlyDictionary<string, FrameAnswer> answers,
        IReadOnlyDictionary<string, string> answerFailures,
        HarnessSettings settings,
        IReadOnlyDictionary<string, CheckPolicy> policy,
        IReadOnlyList<Suppression> suppressions)
    {
        Version = version;
        TracksLatest = tracksLatest;
        Answers = answers;
        AnswerFailures = answerFailures;
        Settings = settings;
        Policy = policy;
        Suppressions = suppressions;
    }

    public int Version { get; }

    public bool TracksLatest { get; }

    public IReadOnlyDictionary<string, FrameAnswer> Answers { get; }

    public IReadOnlyDictionary<string, string> AnswerFailures { get; }

    public HarnessSettings Settings { get; }

    public IReadOnlyDictionary<string, CheckPolicy> Policy { get; }

    public IReadOnlyList<Suppression> Suppressions { get; }

    public FrameAnswer? Answered(string key)
        => Answers.TryGetValue(key, out var answer) ? answer : null;

    public string? AnswerFailure(string key)
        => AnswerFailures.TryGetValue(key, out var failure) ? failure : null;

    public bool IncludesQuestion(int introducedIn)
        => TracksLatest || introducedIn <= Version;

    public CheckPolicy PolicyFor(string checkId, string group)
    {
        if (Policy.TryGetValue(checkId, out var byId))
        {
            return byId;
        }

        return Policy.TryGetValue(group, out var byGroup) ? byGroup : CheckPolicy.Default;
    }

    /// <summary>
    /// Reads the tracked config and validates its envelope before preserving per-answer results.
    /// An untracked config does not exist for
    /// the harness, the same as any untracked file: what verifies a repository has to be
    /// part of it. Every failure names what to fix rather than degrading to a default;
    /// answer failures stay local, while failures that make policy unreliable remain global.
    /// </summary>
    public static (HarnessConfig? Config, string? Failure) Load(
        GitRepository repository,
        IReadOnlyList<IRepositoryCheck> checks)
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
        IReadOnlyList<IRepositoryCheck> checks)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return Invalid("the document is not a JSON object");
        }

        foreach (var property in root.EnumerateObject())
        {
            if (!TopLevelKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                return Invalid($"'{property.Name}' is not a key this harness reads "
                    + $"(expected {string.Join(", ", TopLevelKeys)})");
            }
        }

        var (configVersion, tracksLatest, versionFailure) = ReadVersion(root);
        if (versionFailure is not null)
        {
            return Invalid(versionFailure);
        }

        var (answers, answerFailures, answerFailure) = ReadAnswers(
            root,
            Questions(checks),
            configVersion,
            tracksLatest);
        if (answerFailure is not null)
        {
            return (null, answerFailure);
        }

        var selectors = Selectors(checks);
        var (settings, settingsFailure) = HarnessSettingsReader.Read(root);
        if (settings is null)
        {
            return (null, Failure(settingsFailure!));
        }

        var (policy, policyFailure) = ReadPolicy(root, selectors);
        if (policy is null)
        {
            return (null, policyFailure);
        }

        var (suppressions, suppressionFailure) = ReadSuppressions(root, selectors);
        return suppressions is null
            ? (null, suppressionFailure)
            : (new HarnessConfig(
                configVersion,
                tracksLatest,
                answers!,
                answerFailures!,
                settings,
                policy,
                suppressions), null);
    }

    private static (int Version, bool TracksLatest, string? Failure) ReadVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out var declared))
        {
            return (default, false, $"'version' must be {CurrentVersion} or \"latest\"");
        }

        if (declared.ValueKind == JsonValueKind.String
            && string.Equals(declared.GetString(), "latest", StringComparison.Ordinal))
        {
            return (CurrentVersion, true, null);
        }

        if (declared.ValueKind != JsonValueKind.Number || !declared.TryGetInt32(out var version))
        {
            return (default, false, $"'version' must be {CurrentVersion} or \"latest\"");
        }

        if (version > CurrentVersion)
        {
            return (default, false, $"'version' is {version}, newer than this harness supports "
                + $"(latest is {CurrentVersion}); update the harness before checking this repository");
        }

        if (version < MinimumVersion)
        {
            return (default, false, $"'version' {version} is no longer supported; "
                + $"use {CurrentVersion} or \"latest\"");
        }

        return (version, false, null);
    }

    private static (
        Dictionary<string, FrameAnswer>? Answers,
        Dictionary<string, string>? AnswerFailures,
        string? Failure) ReadAnswers(
        JsonElement root,
        IReadOnlyList<FrameQuestion> questions,
        int version,
        bool tracksLatest)
    {
        if (!root.TryGetProperty("answers", out var declared))
        {
            return (null, null, Failure("'answers' must be an object containing every question this harness asks"));
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, null, Failure("'answers' must be an object"));
        }

        var knownKeys = questions.Select(question => question.Key).ToList();
        var answers = new Dictionary<string, FrameAnswer>(StringComparer.Ordinal);
        var answerFailures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in declared.EnumerateObject())
        {
            var question = questions.FirstOrDefault(candidate => candidate.Key == property.Name);
            if (question is null)
            {
                return (null, null, Failure($"'answers.{property.Name}' is not a question this harness asks "
                    + $"(expected {string.Join(", ", knownKeys)})"));
            }

            // A pinned version is a schema snapshot, not merely a minimum required set.
            // Rejecting later fields catches accidental partial upgrades in either direction.
            if (!tracksLatest && question.IntroducedIn > version)
            {
                return (null, null, Failure($"'answers.{property.Name}' belongs to version "
                    + $"{question.IntroducedIn}, but this repository pins version {version}"));
            }

            var (answer, failure) = ReadAnswer(property.Name, property.Value);
            if (answer is null)
            {
                answerFailures[property.Name] = LocalAnswerFailure(failure!);
                continue;
            }

            answers[property.Name] = answer;
        }

        foreach (var question in questions.Where(question => tracksLatest || question.IntroducedIn <= version))
        {
            if (!answers.ContainsKey(question.Key) && !answerFailures.ContainsKey(question.Key))
            {
                answerFailures[question.Key] = $"'{FileName}' has an incomplete answer: "
                    + $"'answers.{question.Key}' is missing.";
            }
        }

        return (answers, answerFailures, null);
    }

    private static string LocalAnswerFailure(string failure)
    {
        var globalPrefix = $"'{FileName}' is not a valid harness frame: ";
        var detail = failure.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? failure[globalPrefix.Length..]
            : failure;
        return $"'{FileName}' has an invalid answer: {detail}";
    }

    /// <summary>
    /// One answer. The four forms are mutually exclusive on purpose: a repository that
    /// gives an address and also claims absence has not answered, it has contradicted
    /// itself, and the harness will not choose which half to believe.
    /// </summary>
    private static (FrameAnswer? Answer, string? Failure) ReadAnswer(string key, JsonElement value)
    {
        var at = $"answers.{key}";
        if (value.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure($"'{at}' must be an object"));
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("paths" or "present" or "applicable" or "reason"))
            {
                return (null, Failure($"'{at}.{property.Name}' is not a key this harness reads "
                    + "(expected paths, present, applicable, reason)"));
            }
        }

        var reason = ReadString(value, "reason");
        var hasPaths = value.TryGetProperty("paths", out var paths);
        var hasPresent = value.TryGetProperty("present", out var present);

        if (value.TryGetProperty("applicable", out var applicable)
            && applicable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, Failure($"'{at}.applicable' must be true or false"));
        }

        var notApplicable = applicable.ValueKind == JsonValueKind.False;

        if (notApplicable)
        {
            if (hasPaths || hasPresent)
            {
                return (null, Failure($"'{at}' declares the question not applicable and also answers it"));
            }

            return string.IsNullOrWhiteSpace(reason)
                ? (null, Failure($"'{at}' needs a non-empty 'reason' saying why the question does not apply"))
                : (new FrameAnswer(key, FrameAnswerKind.NotApplicable, [], reason.Trim()), null);
        }

        if (hasPaths && hasPresent)
        {
            return (null, Failure($"'{at}' gives both 'paths' and 'present'; an address is already an answer"));
        }

        if (hasPaths)
        {
            var (addresses, failure) = ReadPaths(at, paths);
            return addresses is null
                ? (null, failure)
                : (new FrameAnswer(key, FrameAnswerKind.Located, addresses, reason?.Trim()), null);
        }

        if (!hasPresent || present.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, Failure($"'{at}' must give 'paths', or 'present' as true or false, "
                + "or 'applicable' as false"));
        }

        // An address needs no words; a claim without one does. This is where the harness
        // asks for the sentence a reviewer would have asked for anyway.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (null, Failure($"'{at}' answers without an address, so it needs a non-empty 'reason'"));
        }

        return (
            new FrameAnswer(
                key,
                present.ValueKind == JsonValueKind.True ? FrameAnswerKind.Present : FrameAnswerKind.Absent,
                [],
                reason.Trim()),
            null);
    }

    private static (List<string>? Paths, string? Failure) ReadPaths(string at, JsonElement paths)
    {
        if (paths.ValueKind != JsonValueKind.Array)
        {
            return (null, Failure($"'{at}.paths' must be an array of repository-relative paths"));
        }

        var addresses = new List<string>();
        foreach (var path in paths.EnumerateArray())
        {
            var value = path.ValueKind == JsonValueKind.String ? path.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, Failure($"'{at}.paths' must hold non-empty strings"));
            }

            addresses.Add(value.Trim().TrimEnd('/'));
        }

        return addresses.Count == 0
            ? (null, Failure($"'{at}.paths' is empty; declare 'present' instead of an empty address list"))
            : (addresses, null);
    }

    private static (Dictionary<string, CheckPolicy>? Policy, string? Failure) ReadPolicy(
        JsonElement root,
        IReadOnlyList<string> selectors)
    {
        var policy = new Dictionary<string, CheckPolicy>(StringComparer.Ordinal);
        if (!root.TryGetProperty("policy", out var declared))
        {
            return (policy, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, Failure("'policy' must be an object"));
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!selectors.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, Failure($"'policy.{property.Name}' is not a check or group this harness ships"));
            }

            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            var parsed = value switch
            {
                "required" => CheckPolicy.Required,
                "advisory" => CheckPolicy.Advisory,
                "off" => CheckPolicy.Off,
                _ => CheckPolicy.Default,
            };

            if (parsed == CheckPolicy.Default)
            {
                return (null, Failure($"'policy.{property.Name}' must be required, advisory or off"));
            }

            policy[property.Name] = parsed;
        }

        return (policy, null);
    }

    private static (List<Suppression>? Suppressions, string? Failure) ReadSuppressions(
        JsonElement root,
        IReadOnlyList<string> selectors)
    {
        var suppressions = new List<Suppression>();
        if (!root.TryGetProperty("suppress", out var declared))
        {
            return (suppressions, null);
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            return (null, Failure("'suppress' must be an array"));
        }

        var index = 0;
        foreach (var element in declared.EnumerateArray())
        {
            var at = $"suppress[{index++}]";
            if (element.ValueKind != JsonValueKind.Object)
            {
                return (null, Failure($"'{at}' must be an object"));
            }

            foreach (var property in element.EnumerateObject())
            {
                if (property.Name is not ("check" or "location" or "reason"))
                {
                    return (null, Failure($"'{at}.{property.Name}' is not a key this harness reads "
                        + "(expected check, location, reason)"));
                }
            }

            var check = ReadString(element, "check");
            var location = ReadString(element, "location");
            var reason = ReadString(element, "reason");

            if (string.IsNullOrWhiteSpace(check) || !selectors.Contains(check, StringComparer.Ordinal))
            {
                return (null, Failure($"'{at}.check' must name a check or group this harness ships"));
            }

            if (string.IsNullOrWhiteSpace(location))
            {
                return (null, Failure($"'{at}.location' must be a non-empty repository-relative path"));
            }

            // The whole point of a named exception is the sentence that justifies it. A
            // suppression without one is an invisible exception, which is the thing this
            // mechanism exists to prevent.
            if (string.IsNullOrWhiteSpace(reason))
            {
                return (null, Failure($"'{at}.reason' must say why this finding is accepted"));
            }

            suppressions.Add(new Suppression(check.Trim(), location.Trim().TrimEnd('/'), reason.Trim()));
        }

        return (suppressions, null);
    }

    private static string? ReadString(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    private static List<FrameQuestion> Questions(IReadOnlyList<IRepositoryCheck> checks)
        => checks
            .OfType<FrameQuestionCheck>()
            .Select(check => new FrameQuestion(check.AnswerKey, check.IntroducedIn))
            .ToList();

    private sealed record FrameQuestion(string Key, int IntroducedIn);

    public const string FrameGroup = "frame";

    private static List<string> Selectors(IReadOnlyList<IRepositoryCheck> checks)
        => checks.Select(check => check.Id)
            .Concat(checks.Select(check => check.Group))
            .Distinct(StringComparer.Ordinal)
            .ToList();

    private static (HarnessConfig?, string?) Invalid(string detail) => (null, Failure(detail));

    private static string Failure(string detail) => $"'{FileName}' is not a valid harness frame: {detail}.";

    /// <summary>
    /// The smallest config that answers everything, shown whenever there is none. A reader
    /// who has never seen this file should not have to find documentation to start.
    /// </summary>
    public static string Template =>
        """
        A minimal .harness.json, committed at the repository root:

          {
            "version": 2,
            "answers": {
              "tests.unit": { "paths": ["tests/Unit"] },
              "tests.integration": { "present": false, "reason": "no external dependencies yet" },
              "tests.architecture": { "present": false, "reason": "planned" },
              "format": { "paths": [".editorconfig"] },
              "lint": { "present": true, "reason": "analyzers enabled in Directory.Build.props" },
              "build": { "paths": ["Repository.sln"] },
              "typecheck": { "applicable": false, "reason": "no web stack" }
            },
            "settings": {
              "comments.csharp": {
                "minimumCommentLines": 10,
                "percentageLimit": 25
              },
              "maintainability.csharp": {
                "fileLines": 400,
                "typeLines": 300,
                "methodLines": 60,
                "branches": 12,
                "constructorParameters": 6,
                "publicMembers": 25,
                "importFanOut": 20
              }
            }
          }

        Run `harness explain <check-id>` for what one answer means and how it is reported.
        """;
}

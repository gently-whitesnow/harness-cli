using System.Text.Json;

namespace Harness.Config;

/// <summary>
/// Reads the repository's answers. A failure in one answer stays on that answer: the rest of
/// the frame is still readable, and the question at fault reports its own problem instead of
/// taking the whole run down with it.
/// </summary>
internal static class FrameAnswerReader
{
    public static (
        Dictionary<string, FrameAnswer>? Answers,
        Dictionary<string, string>? Failures,
        string? Failure) Read(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> questions,
        int version,
        bool tracksLatest)
    {
        if (!root.TryGetProperty("answers", out var declared))
        {
            return (null, null, ConfigJson.Failure(
                "'answers' must be an object containing every question this harness asks"));
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, null, ConfigJson.Failure("'answers' must be an object"));
        }

        var answers = new Dictionary<string, FrameAnswer>(StringComparer.Ordinal);
        var failures = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var property in declared.EnumerateObject())
        {
            var rejected = Accept(property, questions, version, tracksLatest, answers, failures);
            if (rejected is not null)
            {
                return (null, null, rejected);
            }
        }

        foreach (var question in questions.Where(question => tracksLatest || question.IntroducedIn <= version))
        {
            if (!answers.ContainsKey(question.AnswerKey!) && !failures.ContainsKey(question.AnswerKey!))
            {
                failures[question.AnswerKey!] = $"'{HarnessConfig.FileName}' has an incomplete answer: "
                    + $"'answers.{question.AnswerKey}' is missing.";
            }
        }

        return (answers, failures, null);
    }

    private static string? Accept(
        JsonProperty property,
        IReadOnlyList<CheckDescriptor> questions,
        int version,
        bool tracksLatest,
        Dictionary<string, FrameAnswer> answers,
        Dictionary<string, string> failures)
    {
        var question = questions.FirstOrDefault(candidate => candidate.AnswerKey == property.Name);
        if (question is null)
        {
            return ConfigJson.Failure($"'answers.{property.Name}' is not a question this harness asks "
                + $"(expected {string.Join(", ", questions.Select(candidate => candidate.AnswerKey))})");
        }

        // A pinned version is a schema snapshot, not merely a minimum required set.
        // Rejecting later fields catches accidental partial upgrades in either direction.
        if (!tracksLatest && question.IntroducedIn > version)
        {
            return ConfigJson.Failure($"'answers.{property.Name}' belongs to version "
                + $"{question.IntroducedIn}, but this repository pins version {version}");
        }

        var (answer, failure) = ReadAnswer(property.Name, property.Value);
        if (answer is null)
        {
            failures[property.Name] = Local(failure!);
            return null;
        }

        answers[property.Name] = answer;
        return null;
    }

    private static string Local(string failure)
    {
        var globalPrefix = $"'{HarnessConfig.FileName}' is not a valid harness frame: ";
        var detail = failure.StartsWith(globalPrefix, StringComparison.Ordinal)
            ? failure[globalPrefix.Length..]
            : failure;
        return $"'{HarnessConfig.FileName}' has an invalid answer: {detail}";
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
            return (null, ConfigJson.Failure($"'{at}' must be an object"));
        }

        foreach (var property in value.EnumerateObject())
        {
            if (property.Name is not ("paths" or "present" or "applicable" or "reason"))
            {
                return (null, ConfigJson.Failure($"'{at}.{property.Name}' is not a key this harness reads "
                    + "(expected paths, present, applicable, reason)"));
            }
        }

        if (value.TryGetProperty("applicable", out var applicable)
            && applicable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, ConfigJson.Failure($"'{at}.applicable' must be true or false"));
        }

        return applicable.ValueKind == JsonValueKind.False
            ? NotApplicable(key, at, value)
            : Answered(key, at, value);
    }

    private static (FrameAnswer? Answer, string? Failure) NotApplicable(string key, string at, JsonElement value)
    {
        if (value.TryGetProperty("paths", out _) || value.TryGetProperty("present", out _))
        {
            return (null, ConfigJson.Failure($"'{at}' declares the question not applicable and also answers it"));
        }

        var reason = ConfigJson.String(value, "reason");
        return string.IsNullOrWhiteSpace(reason)
            ? (null, ConfigJson.Failure($"'{at}' needs a non-empty 'reason' saying why the question does not apply"))
            : (new FrameAnswer(key, FrameAnswerKind.NotApplicable, [], reason.Trim()), null);
    }

    private static (FrameAnswer? Answer, string? Failure) Answered(string key, string at, JsonElement value)
    {
        var reason = ConfigJson.String(value, "reason");
        var hasPaths = value.TryGetProperty("paths", out var paths);
        var hasPresent = value.TryGetProperty("present", out var present);

        if (hasPaths && hasPresent)
        {
            return (null, ConfigJson.Failure(
                $"'{at}' gives both 'paths' and 'present'; an address is already an answer"));
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
            return (null, ConfigJson.Failure($"'{at}' must give 'paths', or 'present' as true or false, "
                + "or 'applicable' as false"));
        }

        // An address needs no words; a claim without one does. This is where the harness
        // asks for the sentence a reviewer would have asked for anyway.
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (null, ConfigJson.Failure($"'{at}' answers without an address, so it needs a non-empty 'reason'"));
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
            return (null, ConfigJson.Failure($"'{at}.paths' must be an array of repository-relative paths"));
        }

        var addresses = new List<string>();
        foreach (var path in paths.EnumerateArray())
        {
            var value = path.ValueKind == JsonValueKind.String ? path.GetString() : null;
            if (string.IsNullOrWhiteSpace(value))
            {
                return (null, ConfigJson.Failure($"'{at}.paths' must hold non-empty strings"));
            }

            addresses.Add(value.Trim().TrimEnd('/'));
        }

        return addresses.Count == 0
            ? (null, ConfigJson.Failure($"'{at}.paths' is empty; declare 'present' instead of an empty address list"))
            : (addresses, null);
    }
}

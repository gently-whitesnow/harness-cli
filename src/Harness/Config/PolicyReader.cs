using System.Text.Json;

namespace Harness.Config;

/// <summary>
/// Reads repository policy around the default: applicability switches a whole family off,
/// while policy softens or disables a check. Every entry has to name something this harness
/// ships, and every applicability answer has to say why.
/// </summary>
internal static class PolicyReader
{
    public static (Dictionary<string, ApplicabilityAnswer>? Applicability, string? Failure) ReadApplicability(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var answers = new Dictionary<string, ApplicabilityAnswer>(StringComparer.Ordinal);
        if (!root.TryGetProperty("applicability", out var declared))
        {
            return (answers, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("'applicability' must be an object"));
        }

        var known = checks.Select(check => check.Applicability).Where(key => key is not null).ToHashSet();
        foreach (var property in declared.EnumerateObject())
        {
            var (answer, failure) = ReadApplicabilityEntry(property, known);
            if (answer is null)
            {
                return (null, failure);
            }

            answers[property.Name] = answer;
        }

        return (answers, null);
    }

    private static (ApplicabilityAnswer? Answer, string? Failure) ReadApplicabilityEntry(
        JsonProperty property,
        HashSet<string?> known)
    {
        var at = $"applicability.{property.Name}";
        if (!known.Contains(property.Name))
        {
            return (null, ConfigJson.Failure($"'{at}' is not an applicability this harness ships"));
        }

        if (property.Value.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure($"'{at}' must be an object"));
        }

        foreach (var member in property.Value.EnumerateObject())
        {
            if (member.Name is not ("applicable" or "reason"))
            {
                return (null, ConfigJson.Failure($"'{at}.{member.Name}' is not a key this harness reads "
                    + "(expected applicable, reason)"));
            }
        }

        if (!property.Value.TryGetProperty("applicable", out var applicable)
            || applicable.ValueKind != JsonValueKind.False)
        {
            return (null, ConfigJson.Failure($"'{at}.applicable' must be false; omit the entry when it applies"));
        }

        var reason = ConfigJson.String(property.Value, "reason");
        return string.IsNullOrWhiteSpace(reason)
            ? (null, ConfigJson.Failure($"'{at}.reason' must say why these checks do not apply"))
            : (new ApplicabilityAnswer(property.Name, reason.Trim()), null);
    }

    public static (Dictionary<string, CheckPolicy>? Policy, string? Failure) ReadPolicy(
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
            return (null, ConfigJson.Failure("'policy' must be an object"));
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (property.Name is "maintainability.csharp" or "cohesion.csharp")
            {
                return (null, ConfigJson.Failure(
                    $"'policy.{property.Name}' was removed in harness 2.0; remove this entry"));
            }

            if (!selectors.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, ConfigJson.Failure(
                    $"'policy.{property.Name}' is not a check or group this harness ships"));
            }

            var value = property.Value.ValueKind == JsonValueKind.String ? property.Value.GetString() : null;
            var parsed = value switch
            {
                "required" => CheckPolicy.Required,
                "advisory" => CheckPolicy.Advisory,
                "off" => CheckPolicy.Off,
                _ => (CheckPolicy?)null,
            };

            if (parsed is null)
            {
                return (null, ConfigJson.Failure(
                    $"'policy.{property.Name}' must be required, advisory or off"));
            }

            policy[property.Name] = parsed.Value;
        }

        return (policy, null);
    }

}

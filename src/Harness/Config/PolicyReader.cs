using System.Text.Json;

namespace Harness.Config;

/// <summary>
/// Reads the three ways a repository softens the default: an applicability that switches a
/// whole family off, a policy that lowers a check, and a named exception that accepts one
/// finding. Every one of them has to name something this harness ships, and every one that
/// speaks without an address has to say why.
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

    public static (List<Suppression>? Suppressions, string? Failure) ReadSuppressions(
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
            return (null, ConfigJson.Failure("'suppress' must be an array"));
        }

        var index = 0;
        foreach (var element in declared.EnumerateArray())
        {
            var (suppression, failure) = ReadSuppression(element, $"suppress[{index++}]", selectors);
            if (suppression is null)
            {
                return (null, failure);
            }

            suppressions.Add(suppression);
        }

        return (suppressions, null);
    }

    private static (Suppression? Suppression, string? Failure) ReadSuppression(
        JsonElement element,
        string at,
        IReadOnlyList<string> selectors)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure($"'{at}' must be an object"));
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("check" or "location" or "reason"))
            {
                return (null, ConfigJson.Failure($"'{at}.{property.Name}' is not a key this harness reads "
                    + "(expected check, location, reason)"));
            }
        }

        var check = ConfigJson.String(element, "check");
        var location = ConfigJson.String(element, "location");
        var reason = ConfigJson.String(element, "reason");

        if (string.IsNullOrWhiteSpace(check) || !selectors.Contains(check, StringComparer.Ordinal))
        {
            return (null, ConfigJson.Failure($"'{at}.check' must name a check or group this harness ships"));
        }

        if (string.IsNullOrWhiteSpace(location))
        {
            return (null, ConfigJson.Failure($"'{at}.location' must be a non-empty repository-relative path"));
        }

        // The whole point of a named exception is the sentence that justifies it. A
        // suppression without one is an invisible exception, which is the thing this
        // mechanism exists to prevent.
        return string.IsNullOrWhiteSpace(reason)
            ? (null, ConfigJson.Failure($"'{at}.reason' must say why this finding is accepted"))
            : (new Suppression(check.Trim(), location.Trim().TrimEnd('/'), reason.Trim()), null);
    }
}

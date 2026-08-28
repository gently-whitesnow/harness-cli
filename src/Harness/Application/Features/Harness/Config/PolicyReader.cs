using System.Text.Json;

namespace Harness.Config;

/// <summary>
/// Reads explicit repository applicability and policy. Every shipped axis and check must be
/// visible in the frame, so adding a check cannot silently inherit a hidden default.
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
            return (null, ConfigJson.Failure("'applicability' must explicitly list every shipped axis"));
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("'applicability' must be an object"));
        }

        var known = checks.Select(check => check.Applicability)
            .Where(key => key is not null)
            .Cast<string>()
            .ToHashSet(StringComparer.Ordinal);
        foreach (var property in declared.EnumerateObject())
        {
            var (answer, failure) = ReadApplicabilityEntry(property, known);
            if (answer is null)
            {
                return (null, failure);
            }

            answers[property.Name] = answer;
        }

        var missing = known.Except(answers.Keys, StringComparer.Ordinal).Order(StringComparer.Ordinal).ToList();
        if (missing.Count > 0)
        {
            return (null, ConfigJson.Failure(
                $"'applicability' is missing explicit entries: {string.Join(", ", missing)}"));
        }

        return (answers, null);
    }

    private static (ApplicabilityAnswer? Answer, string? Failure) ReadApplicabilityEntry(
        JsonProperty property,
        HashSet<string> known)
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
            || applicable.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, ConfigJson.Failure($"'{at}.applicable' must be true or false"));
        }

        var reason = ConfigJson.String(property.Value, "reason");
        var isApplicable = applicable.ValueKind == JsonValueKind.True;
        if (!isApplicable && string.IsNullOrWhiteSpace(reason))
        {
            return (null, ConfigJson.Failure($"'{at}.reason' must say why these checks do not apply"));
        }

        if (isApplicable && property.Value.TryGetProperty("reason", out _))
        {
            return (null, ConfigJson.Failure($"'{at}.reason' is only valid when applicable is false"));
        }

        return (new ApplicabilityAnswer(property.Name, isApplicable, reason?.Trim()), null);
    }

    public static (Dictionary<string, CheckPolicy>? Policy, string? Failure) ReadPolicy(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var policy = new Dictionary<string, CheckPolicy>(StringComparer.Ordinal);
        if (!root.TryGetProperty("policy", out var declared))
        {
            return (null, ConfigJson.Failure("'policy' must explicitly list every shipped check"));
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

            if (!checks.Any(check => check.Id == property.Name))
            {
                return (null, ConfigJson.Failure(
                    $"'policy.{property.Name}' is not a check this harness ships"));
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

        var missing = checks.Select(check => check.Id)
            .Except(policy.Keys, StringComparer.Ordinal)
            .Order(StringComparer.Ordinal)
            .ToList();
        if (missing.Count > 0)
        {
            return (null, ConfigJson.Failure(
                $"'policy' is missing explicit checks: {string.Join(", ", missing)}"));
        }

        foreach (var required in new[] { "architecture.sliced-dotnet", "complexity.csharp" })
        {
            if (policy[required] != CheckPolicy.Required)
            {
                var explanation = required == "architecture.sliced-dotnet"
                    ? "cannot soften or disable blocking architecture invariants"
                    : "cannot soften or disable the blocking ratchet budget";
                return (null, ConfigJson.Failure($"'policy.{required}' {explanation}"));
            }
        }

        return (policy, null);
    }

}

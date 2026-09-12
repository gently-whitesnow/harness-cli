using System.Text.Json;

namespace Harness.Config;

/// <summary>
/// Reads every comparison point a repository is allowed to move. A section is expected for
/// exactly the checks the policy names: a number nobody applies is worse than none, because
/// the repository believes it has been configured, and a check in the frame without its
/// numbers would run on a hidden default the tracked file does not show.
/// </summary>
internal static class HarnessSettingsReader
{
    public static (HarnessSettings? Settings, string? Failure) Read(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks,
        IReadOnlyCollection<string> policyIds)
    {
        if (!root.TryGetProperty("settings", out var declared))
        {
            return (null, "'settings' must be an object holding 'commits' and one section per configurable check in 'policy'");
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, "'settings' must be an object");
        }

        var configurable = checks.Where(HarnessSettings.HasSection).ToDictionary(check => check.Id, StringComparer.Ordinal);
        var expected = configurable.Keys.Where(policyIds.Contains).Append(HarnessSettings.CommitsSection).ToList();
        var retiredFailure = Retired(declared);
        if (retiredFailure is not null)
        {
            return (null, retiredFailure);
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (expected.Contains(property.Name, StringComparer.Ordinal))
            {
                continue;
            }

            return configurable.ContainsKey(property.Name)
                ? (null, $"'settings.{property.Name}' is declared, but 'policy' does not mention {property.Name}; "
                    + "add the policy entry or remove the section — a setting nobody applies is not configuration")
                : (null, $"'settings.{property.Name}' is not configurable "
                    + $"(expected {string.Join(", ", expected)})");
        }

        var missing = expected.Where(section => !declared.TryGetProperty(section, out _)).ToList();
        if (missing.Count > 0)
        {
            return (null, $"'settings' is missing explicit sections for checks in 'policy': {string.Join(", ", missing)}");
        }

        return Assemble(declared, expected.Select(id => configurable.GetValueOrDefault(id)).Where(check => check is not null).ToList()!);
    }

    private static string? Retired(JsonElement declared)
    {
        if (declared.TryGetProperty("dependencies.csharp", out _))
        {
            return "'settings.dependencies.csharp' is not part of the current contract; remove this section. "
                + "The current check proves module cycles and has no comparison points";
        }

        foreach (var removed in new[] { "maintainability.csharp", "cohesion.csharp" })
        {
            if (declared.TryGetProperty(removed, out _))
            {
                return $"'settings.{removed}' was removed in harness 2.0; remove this section";
            }
        }

        return null;
    }

    private static (HarnessSettings? Settings, string? Failure) Assemble(
        JsonElement declared,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var comments = new Dictionary<string, CommentSettings>(StringComparer.Ordinal);
        var duplication = new Dictionary<string, DuplicationSettings>(StringComparer.Ordinal);
        var complexity = new Dictionary<string, ComplexitySettings>(StringComparer.Ordinal);
        foreach (var check in checks)
        {
            switch (check.Group)
            {
                case HarnessSettings.CommentsGroup:
                {
                    var (values, failure) = ReadSection(
                        declared, check.Id, ["minimumCommentLines", "percentageLimit"], [null, 100]);
                    if (values is null)
                    {
                        return (null, failure);
                    }

                    comments[check.Id] = new CommentSettings(values[0], values[1]);
                    break;
                }

                case HarnessSettings.DuplicationGroup:
                {
                    var (values, failure) = ReadSection(declared, check.Id, ["windowLines", "minimumTokens"]);
                    if (values is null)
                    {
                        return (null, failure);
                    }

                    if (values[0] == 0)
                    {
                        return (null, $"'settings.{check.Id}.windowLines' must be a positive integer");
                    }

                    duplication[check.Id] = new DuplicationSettings(values[0], values[1]);
                    break;
                }

                case HarnessSettings.ComplexityGroup:
                {
                    var (values, failure) = ReadComplexity(declared, check.Id);
                    if (values is null)
                    {
                        return (null, failure);
                    }

                    complexity[check.Id] = values;
                    break;
                }

                default:
                    break;
            }
        }

        var (commits, commitFailure) = ReadCommits(declared);
        return commits is null
            ? (null, commitFailure)
            : (new HarnessSettings(comments, duplication, complexity, commits), null);
    }

    private static (ComplexitySettings? Settings, string? Failure) ReadComplexity(JsonElement settings, string section)
    {
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (null, $"'settings.{section}' must be present");
        }

        var failure = ValidateObject(declared, section, ["averageReachableFiles", "largestCyclicGroupSize"]);
        if (failure is not null)
        {
            return (null, failure);
        }

        var at = $"settings.{section}.averageReachableFiles";
        if (!declared.TryGetProperty("averageReachableFiles", out var reach))
        {
            return (null, $"'{at}' must be present");
        }

        if (reach.ValueKind != JsonValueKind.Number
            || !reach.TryGetDouble(out var averageReachableFiles)
            || !double.IsFinite(averageReachableFiles)
            || averageReachableFiles < 1)
        {
            return (null, $"'{at}' must be a number of files not below 1; a file always reaches itself");
        }

        var (largestCyclicGroupSize, cyclicGroupFailure) = ReadInt(declared, section, "largestCyclicGroupSize", null);
        return cyclicGroupFailure is not null
            ? (null, cyclicGroupFailure)
            : (new ComplexitySettings(averageReachableFiles, largestCyclicGroupSize), null);
    }

    private static (CommitSettings? Settings, string? Failure) ReadCommits(JsonElement settings)
    {
        const string Commits = HarnessSettings.CommitsSection;
        if (!settings.TryGetProperty(Commits, out var declared))
        {
            return (null, $"'settings.{Commits}' must be present");
        }

        var failure = ValidateObject(declared, Commits, ["language", "requireSetup"]);
        if (failure is not null)
        {
            return (null, failure);
        }

        if (!declared.TryGetProperty("language", out var declaredLanguage))
        {
            return (null, "'settings.commits.language' must be present");
        }

        var value = declaredLanguage.ValueKind == JsonValueKind.String ? declaredLanguage.GetString() : null;
        if (value is not ("en" or "ru"))
        {
            return (null, "'settings.commits.language' must be 'en' or 'ru'");
        }

        var language = value == "ru" ? CommitLanguage.Russian : CommitLanguage.English;

        if (!declared.TryGetProperty("requireSetup", out var declaredRequirement))
        {
            return (null, "'settings.commits.requireSetup' must be present");
        }

        if (declaredRequirement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
        {
            return (null, "'settings.commits.requireSetup' must be true or false");
        }

        var requireSetup = declaredRequirement.ValueKind == JsonValueKind.True;
        return (new CommitSettings(language, requireSetup), null);
    }

    private static (int[]? Values, string? Failure) ReadSection(
        JsonElement settings,
        string section,
        string[] known,
        int?[]? maximum = null)
    {
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (null, $"'settings.{section}' must be present");
        }

        var failure = ValidateObject(declared, section, known);
        if (failure is not null)
        {
            return (null, failure);
        }

        var values = new int[known.Length];
        for (var index = 0; index < known.Length; index++)
        {
            if (!declared.TryGetProperty(known[index], out _))
            {
                return (null, $"'settings.{section}.{known[index]}' must be present");
            }

            var (value, valueFailure) = ReadInt(
                declared, section, known[index], maximum?[index]);
            if (valueFailure is not null)
            {
                return (null, valueFailure);
            }

            values[index] = value;
        }

        return (values, null);
    }

    private static string? ValidateObject(
        JsonElement declared,
        string section,
        IReadOnlyList<string> known)
    {
        if (declared.ValueKind != JsonValueKind.Object)
        {
            return $"'settings.{section}' must be an object";
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                return $"'settings.{section}.{property.Name}' is not a setting this check reads "
                    + $"(expected {string.Join(", ", known)})";
            }
        }

        return null;
    }

    private static (int Value, string? Failure) ReadInt(
        JsonElement declared,
        string section,
        string name,
        int? maximum)
    {
        if (!declared.TryGetProperty(name, out var value))
        {
            return (0, $"'settings.{section}.{name}' must be present");
        }

        var at = $"settings.{section}.{name}";
        if (value.ValueKind != JsonValueKind.Number || !value.TryGetInt32(out var parsed) || parsed < 0)
        {
            return (0, $"'{at}' must be a non-negative integer");
        }

        return maximum is not null && parsed > maximum
            ? (0, $"'{at}' must not exceed {maximum}")
            : (parsed, null);
    }
}

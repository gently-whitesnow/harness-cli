using System.Text.Json;

namespace Harness.Config;

internal static class HarnessSettingsReader
{
    public static (HarnessSettings? Settings, string? Failure) Read(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var declared))
        {
            return (HarnessSettings.Default, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, "'settings' must be an object");
        }

        var known = new[] { "comments.csharp", "maintainability.csharp", "commits" };
        foreach (var property in declared.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, $"'settings.{property.Name}' is not configurable "
                    + $"(expected {string.Join(", ", known)})");
            }
        }

        var (comments, commentFailure) = ReadComments(declared);
        if (comments is null)
        {
            return (null, commentFailure);
        }

        var (maintainability, maintainabilityFailure) = ReadMaintainability(declared);
        if (maintainability is null)
        {
            return (null, maintainabilityFailure);
        }

        var (commits, commitFailure) = ReadCommits(declared);
        return commits is null
            ? (null, commitFailure)
            : (new HarnessSettings(comments, maintainability, commits), null);
    }

    private static (CommentSettings? Settings, string? Failure) ReadComments(JsonElement settings)
    {
        const string section = "comments.csharp";
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (CommentSettings.Default, null);
        }

        var known = new[] { "minimumCommentLines", "percentageLimit" };
        var failure = ValidateObject(declared, section, known);
        if (failure is not null)
        {
            return (null, failure);
        }

        var defaults = CommentSettings.Default;
        var (minimum, minimumFailure) = ReadInt(
            declared, section, known[0], defaults.MinimumCommentLines);
        if (minimumFailure is not null)
        {
            return (null, minimumFailure);
        }

        var (percentage, percentageFailure) = ReadInt(
            declared, section, known[1], defaults.PercentageLimit, maximum: 100);
        return percentageFailure is null
            ? (new CommentSettings(minimum, percentage), null)
            : (null, percentageFailure);
    }

    private static (MaintainabilitySettings? Settings, string? Failure) ReadMaintainability(
        JsonElement settings)
    {
        const string section = "maintainability.csharp";
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (MaintainabilitySettings.Default, null);
        }

        var known = new[]
        {
            "fileLines", "typeLines", "methodLines", "branches", "constructorParameters",
            "publicMembers", "importFanOut",
        };
        var failure = ValidateObject(declared, section, known);
        if (failure is not null)
        {
            return (null, failure);
        }

        var defaults = MaintainabilitySettings.Default;
        var fallback = new[]
        {
            defaults.FileLines, defaults.TypeLines, defaults.MethodLines, defaults.Branches,
            defaults.ConstructorParameters, defaults.PublicMembers, defaults.ImportFanOut,
        };
        var values = new int[known.Length];
        for (var index = 0; index < known.Length; index++)
        {
            var (value, valueFailure) = ReadInt(declared, section, known[index], fallback[index]);
            if (valueFailure is not null)
            {
                return (null, valueFailure);
            }

            values[index] = value;
        }

        return (new MaintainabilitySettings(
            values[0], values[1], values[2], values[3], values[4], values[5], values[6]), null);
    }

    private static (CommitSettings? Settings, string? Failure) ReadCommits(JsonElement settings)
    {
        const string section = "commits";
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (CommitSettings.Default, null);
        }

        var known = new[] { "language", "requireSetup" };
        var failure = ValidateObject(declared, section, known);
        if (failure is not null)
        {
            return (null, failure);
        }

        var language = CommitLanguage.English;
        if (declared.TryGetProperty("language", out var declaredLanguage))
        {
            var value = declaredLanguage.ValueKind == JsonValueKind.String ? declaredLanguage.GetString() : null;
            if (value is not ("en" or "ru"))
            {
                return (null, "'settings.commits.language' must be 'en' or 'ru'");
            }

            language = value == "ru" ? CommitLanguage.Russian : CommitLanguage.English;
        }

        var requireSetup = false;
        if (declared.TryGetProperty("requireSetup", out var declaredRequirement))
        {
            if (declaredRequirement.ValueKind is not (JsonValueKind.True or JsonValueKind.False))
            {
                return (null, "'settings.commits.requireSetup' must be true or false");
            }

            requireSetup = declaredRequirement.ValueKind == JsonValueKind.True;
        }

        return (new CommitSettings(language, requireSetup), null);
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
        int fallback,
        int? maximum = null)
    {
        if (!declared.TryGetProperty(name, out var value))
        {
            return (fallback, null);
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

using System.Text.Json;
using Harness.Languages;

namespace Harness.Config;

/// <summary>
/// Reads every comparison point a repository is allowed to move. A setting the harness does
/// not read is a failure and not a silent no-op: a number nobody applies is worse than none,
/// because the repository believes it has been configured.
/// </summary>
internal static class HarnessSettingsReader
{
    private static readonly string Comments = Language.CSharp.Qualify("comments");
    private static readonly string Dependencies = Language.CSharp.Qualify("dependencies");
    private static readonly string Duplication = Language.CSharp.Qualify("duplication");
    private const string Commits = "commits";

    public static (HarnessSettings? Settings, string? Failure) Read(JsonElement root)
    {
        if (!root.TryGetProperty("settings", out var declared))
        {
            return (null, "'settings' must explicitly list every configurable section");
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, "'settings' must be an object");
        }

        if (declared.TryGetProperty(Dependencies, out _))
        {
            return (null, $"'settings.{Dependencies}' is not part of the current contract; remove this section. "
                + "The current check proves module cycles and has no comparison points");
        }

        foreach (var removed in new[] { "maintainability.csharp", "cohesion.csharp" })
        {
            if (declared.TryGetProperty(removed, out _))
            {
                return (null, $"'settings.{removed}' was removed in harness 2.0; remove this section");
            }
        }

        string[] known = [Comments, Duplication, Commits];
        foreach (var property in declared.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, $"'settings.{property.Name}' is not configurable "
                    + $"(expected {string.Join(", ", known)})");
            }
        }

        var missing = known.Where(section => !declared.TryGetProperty(section, out _)).ToList();
        if (missing.Count > 0)
        {
            return (null, $"'settings' is missing explicit sections: {string.Join(", ", missing)}");
        }

        return Assemble(declared);
    }

    private static (HarnessSettings? Settings, string? Failure) Assemble(JsonElement declared)
    {
        var (comments, commentFailure) = ReadSection(
            declared,
            Comments,
            ["minimumCommentLines", "percentageLimit"],
            [null, 100]);
        if (comments is null)
        {
            return (null, commentFailure);
        }

        var (duplication, duplicationFailure) = ReadSection(
            declared,
            Duplication,
            ["windowLines", "minimumTokens"]);
        if (duplication is null)
        {
            return (null, duplicationFailure);
        }

        if (duplication[0] == 0)
        {
            return (null, $"'settings.{Duplication}.windowLines' must be a positive integer");
        }

        var (commits, commitFailure) = ReadCommits(declared);
        return commits is null
            ? (null, commitFailure)
            : (new HarnessSettings(
                new CommentSettings(comments[0], comments[1]),
                new DuplicationSettings(duplication[0], duplication[1]),
                commits), null);
    }

    private static (CommitSettings? Settings, string? Failure) ReadCommits(JsonElement settings)
    {
        if (!settings.TryGetProperty(Commits, out var declared))
        {
            return (null, $"'settings.{Commits}' must be present");
        }

        var failure = ValidateObject(declared, Commits, ["language", "requireSetup"], null);
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
        int?[]? maximum = null,
        IReadOnlyDictionary<string, string>? moved = null)
    {
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (null, $"'settings.{section}' must be present");
        }

        var failure = ValidateObject(declared, section, known, moved);
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
        IReadOnlyList<string> known,
        IReadOnlyDictionary<string, string>? moved)
    {
        if (declared.ValueKind != JsonValueKind.Object)
        {
            return $"'settings.{section}' must be an object";
        }

        foreach (var property in declared.EnumerateObject())
        {
            if (moved is not null && moved.TryGetValue(property.Name, out var destination))
            {
                return $"'settings.{section}.{property.Name}' is now '{destination}'; "
                    + "the measurement moved together with the check that reads it";
            }

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

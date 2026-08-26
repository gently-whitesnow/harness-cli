using System.Text.Json;
using Harness.Commits;
using Harness.Languages;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Reads every comparison point a repository is allowed to move. A setting the harness does
/// not read is a failure and not a silent no-op: a number nobody applies is worse than none,
/// because the repository believes it has been configured.
/// </summary>
internal static class HarnessSettingsReader
{
    private static readonly HarnessVersion DependencyCountsRemovedIn = new(1, 5, 0);
    private static readonly HarnessVersion ContextualWidthCountsRemovedIn = new(1, 6, 0);

    private static readonly string Comments = Language.CSharp.Qualify("comments");
    private static readonly string Maintainability = Language.CSharp.Qualify("maintainability");
    private static readonly string Dependencies = Language.CSharp.Qualify("dependencies");
    private static readonly string Cohesion = Language.CSharp.Qualify("cohesion");
    private static readonly string Duplication = Language.CSharp.Qualify("duplication");
    private const string Commits = "commits";

    public static (HarnessSettings? Settings, string? Failure) Read(JsonElement root, HarnessVersion version)
    {
        var defaults = HarnessSettings.For(version);
        if (!root.TryGetProperty("settings", out var declared))
        {
            return (defaults, null);
        }

        if (declared.ValueKind != JsonValueKind.Object)
        {
            return (null, "'settings' must be an object");
        }

        var readsDependencyCounts = version < DependencyCountsRemovedIn;
        if (!readsDependencyCounts && declared.TryGetProperty(Dependencies, out _))
        {
            return (null, $"'settings.{Dependencies}' was removed in harness 1.5; remove this section. "
                + "The current check proves module cycles and has no comparison points");
        }

        if (!readsDependencyCounts
            && declared.TryGetProperty(Maintainability, out var maintainability)
            && maintainability.ValueKind == JsonValueKind.Object
            && maintainability.TryGetProperty("importFanOut", out _))
        {
            return (null, $"'settings.{Maintainability}.importFanOut' was removed in harness 1.5; "
                + "dependency cycles have no import-count setting");
        }

        string[] known = readsDependencyCounts
            ? [Comments, Maintainability, Dependencies, Cohesion, Duplication, Commits]
            : [Comments, Maintainability, Cohesion, Duplication, Commits];
        foreach (var property in declared.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, $"'settings.{property.Name}' is not configurable "
                    + $"(expected {string.Join(", ", known)})");
            }
        }

        return Assemble(declared, defaults, version, readsDependencyCounts);
    }

    private static (HarnessSettings? Settings, string? Failure) Assemble(
        JsonElement declared,
        HarnessSettings defaults,
        HarnessVersion version,
        bool readsDependencyCounts)
    {
        var (comments, commentFailure) = ReadSection(
            declared,
            Comments,
            ["minimumCommentLines", "percentageLimit"],
            [defaults.Comments.MinimumCommentLines, defaults.Comments.PercentageLimit],
            [null, 100]);
        if (comments is null)
        {
            return (null, commentFailure);
        }

        var (maintainability, maintainabilityFailure) = ReadMaintainability(declared, defaults.Maintainability, version);
        if (maintainability is null)
        {
            return (null, maintainabilityFailure);
        }

        var (dependencies, dependencyFailure) = DependenciesFor(
            declared, defaults.Dependencies, readsDependencyCounts);
        if (dependencies is null)
        {
            return (null, dependencyFailure);
        }

        var (cohesion, cohesionFailure) = ReadSection(
            declared,
            Cohesion,
            ["minimumMembers", "groups"],
            [defaults.Cohesion.MinimumMembers, defaults.Cohesion.Groups]);
        if (cohesion is null)
        {
            return (null, cohesionFailure);
        }

        var (duplication, duplicationFailure) = ReadSection(
            declared,
            Duplication,
            ["windowLines", "minimumTokens"],
            [defaults.Duplication.WindowLines, defaults.Duplication.MinimumTokens]);
        if (duplication is null)
        {
            return (null, duplicationFailure);
        }

        if (duplication[0] == 0)
        {
            return (null, $"'settings.{Duplication}.windowLines' must be a positive integer");
        }

        var (commits, commitFailure) = ReadCommits(declared, defaults.Commits);
        return commits is null
            ? (null, commitFailure)
            : (new HarnessSettings(
                new CommentSettings(comments[0], comments[1]),
                maintainability,
                dependencies,
                new CohesionSettings(cohesion[0], cohesion[1]),
                new DuplicationSettings(duplication[0], duplication[1]),
                commits), null);
    }

    private static (DependencySettings? Settings, string? Failure) DependenciesFor(
        JsonElement declared,
        DependencySettings defaults,
        bool readsDependencyCounts)
        => readsDependencyCounts ? ReadDependencies(declared, defaults) : (defaults, null);

    private static (MaintainabilitySettings? Settings, string? Failure) ReadMaintainability(
        JsonElement declared,
        MaintainabilitySettings defaults,
        HarnessVersion version)
    {
        var readsContextualWidthCounts = version < ContextualWidthCountsRemovedIn;
        if (declared.TryGetProperty(Maintainability, out var maintainability)
            && maintainability.ValueKind == JsonValueKind.Object
            && maintainability.TryGetProperty("branches", out _))
        {
            return (null, $"'settings.{Maintainability}.branches' was removed in harness 1.6; "
                + "remove this key because lexical tokens do not measure control-flow complexity");
        }

        if (!readsContextualWidthCounts
            && declared.TryGetProperty(Maintainability, out maintainability)
            && maintainability.ValueKind == JsonValueKind.Object
            && maintainability.TryGetProperty("publicMembers", out _))
        {
            return (null, $"'settings.{Maintainability}.publicMembers' was removed in harness 1.6; "
                + "remove this key because public surface width is no longer measured");
        }

        if (!readsContextualWidthCounts
            && declared.TryGetProperty(Maintainability, out maintainability)
            && maintainability.ValueKind == JsonValueKind.Object
            && maintainability.TryGetProperty("constructorParameters", out _))
        {
            return (null, $"'settings.{Maintainability}.constructorParameters' was removed in harness 1.6; "
                + "remove this key because raw constructor arity is no longer measured");
        }

        string[] known = readsContextualWidthCounts
            ? ["fileLines", "typeLines", "methodLines", "constructorParameters", "publicMembers"]
            : ["fileLines", "typeLines", "methodLines"];
        int[] fallback = readsContextualWidthCounts
            ? [
                defaults.FileLines, defaults.TypeLines, defaults.MethodLines,
                defaults.ConstructorParameters, defaults.PublicMembers,
            ]
            : [
                defaults.FileLines, defaults.TypeLines, defaults.MethodLines,
            ];
        var (values, failure) = ReadSection(
            declared,
            Maintainability,
            known,
            fallback,
            moved: new Dictionary<string, string>(StringComparer.Ordinal)
            {
                ["importFanOut"] = $"settings.{Dependencies}.externalImports",
            });

        return values is null
            ? (null, failure)
            : (new MaintainabilitySettings(
                values[0], values[1], values[2],
                readsContextualWidthCounts ? values[3] : defaults.ConstructorParameters,
                readsContextualWidthCounts ? values[4] : defaults.PublicMembers), null);
    }

    private static (DependencySettings? Settings, string? Failure) ReadDependencies(
        JsonElement declared,
        DependencySettings defaults)
    {
        var (values, failure) = ReadSection(
            declared,
            Dependencies,
            ["externalImports", "outgoingReferences", "incomingReferences"],
            [defaults.ExternalImports, defaults.OutgoingReferences, defaults.IncomingReferences]);

        return values is null
            ? (null, failure)
            : (new DependencySettings(values[0], values[1], values[2]), null);
    }

    private static (CommitSettings? Settings, string? Failure) ReadCommits(
        JsonElement settings,
        CommitSettings defaults)
    {
        if (!settings.TryGetProperty(Commits, out var declared))
        {
            return (defaults, null);
        }

        var failure = ValidateObject(declared, Commits, ["language", "requireSetup"], null);
        if (failure is not null)
        {
            return (null, failure);
        }

        var language = defaults.Language;
        if (declared.TryGetProperty("language", out var declaredLanguage))
        {
            var value = declaredLanguage.ValueKind == JsonValueKind.String ? declaredLanguage.GetString() : null;
            if (value is not ("en" or "ru"))
            {
                return (null, "'settings.commits.language' must be 'en' or 'ru'");
            }

            language = value == "ru" ? CommitLanguage.Russian : CommitLanguage.English;
        }

        var requireSetup = defaults.RequireSetup;
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

    private static (int[]? Values, string? Failure) ReadSection(
        JsonElement settings,
        string section,
        string[] known,
        int[] fallback,
        int?[]? maximum = null,
        IReadOnlyDictionary<string, string>? moved = null)
    {
        if (!settings.TryGetProperty(section, out var declared))
        {
            return (fallback, null);
        }

        var failure = ValidateObject(declared, section, known, moved);
        if (failure is not null)
        {
            return (null, failure);
        }

        var values = new int[known.Length];
        for (var index = 0; index < known.Length; index++)
        {
            var (value, valueFailure) = ReadInt(
                declared, section, known[index], fallback[index], maximum?[index]);
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
        int fallback,
        int? maximum)
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

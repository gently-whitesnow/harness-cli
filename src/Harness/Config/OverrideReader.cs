using System.Text.Json;
using Harness.Languages;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Reads and fully validates the `overrides` section. An override only exists for checks
/// whose subject lives in a file, its settings keys are the same dictionary the global
/// section reads, and — like every deliberate softening in this frame — it does not speak
/// without a reason.
/// </summary>
internal static class OverrideReader
{
    /// <summary>The release whose contract introduced the section (ADR-0024).</summary>
    public static HarnessVersion Since { get; } = new(1, 1, 0);

    private static readonly Dictionary<string, string[]> SettingsKeys = new(StringComparer.Ordinal)
    {
        [Language.CSharp.Qualify("comments")] = ["minimumCommentLines", "percentageLimit"],
        [Language.CSharp.Qualify("maintainability")] =
            ["fileLines", "typeLines", "methodLines", "branches", "constructorParameters", "publicMembers"],
        [Language.CSharp.Qualify("cohesion")] = ["minimumMembers", "groups"],
    };

    private static readonly string[] OffOnly =
        [Language.CSharp.Qualify("types-per-file"), Language.CSharp.Qualify("duplication")];

    public static (List<PathOverride>? Overrides, string? Failure) Read(JsonElement root, bool included)
    {
        var overrides = new List<PathOverride>();
        if (!root.TryGetProperty("overrides", out var declared))
        {
            return (overrides, null);
        }

        if (!included)
        {
            return (null, ConfigJson.Failure($"'overrides' is part of harness {Since} and this repository "
                + "pins an older release; raise the pin with `harness upgrade` first"));
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            return (null, ConfigJson.Failure("'overrides' must be an array"));
        }

        var index = 0;
        foreach (var element in declared.EnumerateArray())
        {
            var (entry, failure) = ReadEntry(element, $"overrides[{index++}]");
            if (entry is null)
            {
                return (null, failure);
            }

            overrides.Add(entry);
        }

        return (overrides, null);
    }

    private static (PathOverride? Override, string? Failure) ReadEntry(JsonElement element, string at)
    {
        if (element.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure($"'{at}' must be an object"));
        }

        foreach (var property in element.EnumerateObject())
        {
            if (property.Name is not ("check" or "paths" or "reason" or "settings" or "off"))
            {
                return (null, ConfigJson.Failure($"'{at}.{property.Name}' is not a key this harness reads "
                    + "(expected check, paths, reason, and settings or off)"));
            }
        }

        var check = ConfigJson.String(element, "check");
        if (check is null || (!SettingsKeys.ContainsKey(check) && !OffOnly.Contains(check, StringComparer.Ordinal)))
        {
            return (null, ConfigJson.Failure($"'{at}.check' must name a path-scoped check "
                + $"(expected {string.Join(", ", SettingsKeys.Keys.Concat(OffOnly))})"));
        }

        var (paths, pathFailure) = ReadPaths(element, at);
        if (paths is null)
        {
            return (null, pathFailure);
        }

        // A zone norm nobody justified is an invisible exception wearing different clothes;
        // the reason is the part of the record a reviewer can actually argue with.
        var reason = ConfigJson.String(element, "reason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (null, ConfigJson.Failure($"'{at}.reason' must say why this zone lives by a different norm"));
        }

        return ReadAction(element, at, check, paths, reason.Trim());
    }

    private static (IReadOnlyList<string>? Paths, string? Failure) ReadPaths(JsonElement element, string at)
    {
        if (!element.TryGetProperty("paths", out var declared) || declared.ValueKind != JsonValueKind.Array)
        {
            return (null, ConfigJson.Failure($"'{at}.paths' must be an array of repository-relative globs"));
        }

        var paths = new List<string>();
        foreach (var value in declared.EnumerateArray())
        {
            var pattern = value.ValueKind == JsonValueKind.String ? value.GetString() : null;
            if (string.IsNullOrWhiteSpace(pattern) || pattern.StartsWith('/'))
            {
                return (null, ConfigJson.Failure($"'{at}.paths' entries must be non-empty "
                    + "repository-relative globs such as \"src/**/Adapters/**\""));
            }

            paths.Add(pattern.Trim().TrimEnd('/'));
        }

        return paths.Count == 0
            ? (null, ConfigJson.Failure($"'{at}.paths' must name at least one glob"))
            : (paths, null);
    }

    private static (PathOverride? Override, string? Failure) ReadAction(
        JsonElement element,
        string at,
        string check,
        IReadOnlyList<string> paths,
        string reason)
    {
        var hasSettings = element.TryGetProperty("settings", out var settings);
        var hasOff = element.TryGetProperty("off", out var off);
        if (hasSettings == hasOff)
        {
            return (null, ConfigJson.Failure($"'{at}' must carry exactly one of 'settings' or 'off'"));
        }

        if (hasOff)
        {
            return off.ValueKind != JsonValueKind.True
                ? (null, ConfigJson.Failure($"'{at}.off' must be true; omit the key to override settings instead"))
                : (new PathOverride(check, paths, reason, Off: true, Settings: new Dictionary<string, int>()), null);
        }

        if (!SettingsKeys.TryGetValue(check, out var known))
        {
            return (null, ConfigJson.Failure($"'{at}.settings' cannot apply: '{check}' has no settings; "
                + "use 'off': true to exclude the zone"));
        }

        var (values, failure) = ReadValues(settings, at, check, known);
        return values is null
            ? (null, failure)
            : (new PathOverride(check, paths, reason, Off: false, values), null);
    }

    private static (Dictionary<string, int>? Values, string? Failure) ReadValues(
        JsonElement settings,
        string at,
        string check,
        string[] known)
    {
        if (settings.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure($"'{at}.settings' must be an object"));
        }

        var values = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var property in settings.EnumerateObject())
        {
            if (!known.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, ConfigJson.Failure($"'{at}.settings.{property.Name}' is not a setting "
                    + $"'{check}' reads (expected {string.Join(", ", known)})"));
            }

            if (property.Value.ValueKind != JsonValueKind.Number
                || !property.Value.TryGetInt32(out var parsed)
                || parsed < 0)
            {
                return (null, ConfigJson.Failure($"'{at}.settings.{property.Name}' must be a non-negative integer"));
            }

            if (property.Name == "percentageLimit" && parsed > 100)
            {
                return (null, ConfigJson.Failure($"'{at}.settings.{property.Name}' must not exceed 100"));
            }

            values[property.Name] = parsed;
        }

        return values.Count == 0
            ? (null, ConfigJson.Failure($"'{at}.settings' must move at least one number"))
            : (values, null);
    }
}

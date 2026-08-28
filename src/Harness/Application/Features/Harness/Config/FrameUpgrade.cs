using System.Text.Json;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

internal static class FrameUpgrade
{
    public static (string? Report, string? Failure) Raise(IRepository repository, bool dryRun)
    {
        var path = Path.Combine(repository.RootPath, HarnessConfig.FileName);
        if (!repository.TrackedEntries.Any(entry => entry.Path == HarnessConfig.FileName))
        {
            return (null, $"'{HarnessConfig.FileName}' must be tracked before it can be upgraded.");
        }

        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not read '{path}': {exception.Message}");
        }

        var (pin, failure) = ReadPin(text);
        if (pin is null)
        {
            return (null, failure);
        }

        if (pin is "latest" || pin == HarnessVersion.Current.ToString())
        {
            return ($"{HarnessConfig.FileName} already runs contract {pin}; there is no pin to raise.\n", null);
        }

        if (!HarnessVersion.TryParse(pin, out var version))
        {
            return (null, $"'version' is not a harness release: {pin}.");
        }

        if (IsNewer(version, HarnessVersion.Current))
        {
            return (null, $"This binary is harness {HarnessVersion.Current} and cannot migrate the newer pin {pin}; update the harness first.");
        }

        if (!dryRun)
        {
            var rewriteFailure = Rewrite(path, text, pin, HarnessVersion.Current.ToString());
            if (rewriteFailure is not null)
            {
                return (null, rewriteFailure);
            }
        }

        var action = dryRun ? "Would raise" : "Raised";
        return ($$"""
        {{action}} {{HarnessConfig.FileName}} from {{pin}} to {{HarnessVersion.Current}}.
        Contract 2.0 migration:
          removed  maintainability.csharp, cohesion.csharp, suppress, overrides and legacy defaults
          added    architecture: { "standard": "sliced-dotnet/1" } or { "applicable": false, "reason": "..." }
          added    explicit applicability, settings and policy entries for every shipped check
          added    tracked .harness.budget.json with complexity.csharp propagationCost and coreSize
        Review these sections, then run `harness check --verbose`. {{(dryRun ? "Nothing was written." : "Only the pin was changed; repository answers were not guessed.")}}
        """ + "\n", null);
    }

    private static (string? Pin, string? Failure) ReadPin(string text)
    {
        try
        {
            using var document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
            if (document.RootElement.ValueKind != JsonValueKind.Object
                || !document.RootElement.TryGetProperty("version", out var version)
                || version.ValueKind != JsonValueKind.String)
            {
                return (null, "'version' must be a string before the frame can be upgraded.");
            }

            return (version.GetString(), null);
        }
        catch (JsonException exception)
        {
            return (null, $"'{HarnessConfig.FileName}' is not readable as JSON ({exception.Message}).");
        }
    }

    private static bool IsNewer(HarnessVersion candidate, HarnessVersion current)
        => candidate.Major > current.Major
            || (candidate.Major == current.Major && candidate.Minor > current.Minor)
            || (candidate.Major == current.Major
                && candidate.Minor == current.Minor
                && candidate.Patch > current.Patch);

    private static string? Rewrite(string path, string text, string from, string target)
    {
        var current = $"\"{from}\"";
        var key = text.IndexOf("\"version\"", StringComparison.Ordinal);
        var value = key < 0 ? -1 : text.IndexOf(current, key, StringComparison.Ordinal);
        if (value < 0)
        {
            return $"Could not find the pinned value {current} in '{path}'.";
        }

        try
        {
            File.WriteAllText(path, string.Concat(
                text.AsSpan(0, value),
                $"\"{target}\"",
                text.AsSpan(value + current.Length)));
            return null;
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return $"Could not write '{path}': {exception.Message}";
        }
    }
}

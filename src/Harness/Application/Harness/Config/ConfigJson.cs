using System.Text.Json;

namespace Harness.Config;

internal static class ConfigJson
{
    /// <summary>How every tracked frame is parsed: comments and trailing commas are allowed.</summary>
    public static readonly JsonDocumentOptions ParseOptions = new()
    {
        CommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    public static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string Failure(string detail)
        => $"'{HarnessConfig.FileName}' is not a valid harness frame: {detail}.";

    /// <summary>
    /// The dotted path of the first property repeated at any depth, or null. A repeated key
    /// leaves the frame ambiguous — the parser would keep the last value and say nothing — so
    /// the reader refuses it instead of guessing which answer the owner meant.
    /// </summary>
    public static string? DuplicateProperty(JsonElement element)
    {
        if (element.ValueKind == JsonValueKind.Array)
        {
            return element.EnumerateArray().Select(DuplicateProperty).FirstOrDefault(value => value is not null);
        }

        if (element.ValueKind != JsonValueKind.Object)
        {
            return null;
        }

        var names = new HashSet<string>(StringComparer.Ordinal);
        foreach (var property in element.EnumerateObject())
        {
            if (!names.Add(property.Name))
            {
                return property.Name;
            }

            var nested = DuplicateProperty(property.Value);
            if (nested is not null)
            {
                return property.Name + "." + nested;
            }
        }

        return null;
    }
}

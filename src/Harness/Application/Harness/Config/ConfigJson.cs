using System.Text.Json;

namespace Harness.Config;

internal static class ConfigJson
{
    public static string? String(JsonElement element, string name)
        => element.TryGetProperty(name, out var value) && value.ValueKind == JsonValueKind.String
            ? value.GetString()
            : null;

    public static string Failure(string detail)
        => $"'{HarnessConfig.FileName}' is not a valid harness frame: {detail}.";
}

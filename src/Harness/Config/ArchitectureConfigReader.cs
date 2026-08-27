using System.Text.Json;

namespace Harness.Config;

internal static class ArchitectureConfigReader
{
    public const string SlicedDotNet = "sliced-dotnet/1";

    public static (ArchitectureConfig? Architecture, string? Failure) Read(JsonElement root)
    {
        if (!root.TryGetProperty("architecture", out var declared)
            || declared.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure(
                $"'architecture' must be an object selecting standard '{SlicedDotNet}' or declaring applicability false"));
        }

        var members = declared.EnumerateObject().ToList();
        var allowed = members.All(member => member.Name is "standard" or "applicable" or "reason");
        if (!allowed)
        {
            var unknown = members.First(member => member.Name is not ("standard" or "applicable" or "reason"));
            return (null, ConfigJson.Failure(
                $"'architecture.{unknown.Name}' is not a key this harness reads (expected standard, or applicable and reason)"));
        }

        if (declared.TryGetProperty("standard", out var standard))
        {
            if (members.Count != 1)
            {
                return (null, ConfigJson.Failure(
                    "'architecture.standard' cannot be combined with other architecture keys"));
            }

            var name = standard.ValueKind == JsonValueKind.String ? standard.GetString() : null;
            return string.Equals(name, SlicedDotNet, StringComparison.Ordinal)
                ? (new ArchitectureConfig(SlicedDotNet, null), null)
                : (null, ConfigJson.Failure(
                    $"'architecture.standard' must be '{SlicedDotNet}'; '{name ?? standard.ToString()}' is not supported"));
        }

        if (members.Any(member => member.Name is not ("applicable" or "reason")))
        {
            return (null, ConfigJson.Failure("'architecture' must select a standard or declare applicability false"));
        }

        if (!declared.TryGetProperty("applicable", out var applicable)
            || applicable.ValueKind != JsonValueKind.False)
        {
            return (null, ConfigJson.Failure(
                "'architecture.applicable' must be false; select 'sliced-dotnet/1' when architecture applies"));
        }

        var reason = ConfigJson.String(declared, "reason");
        if (string.IsNullOrWhiteSpace(reason))
        {
            return (null, ConfigJson.Failure(
                "'architecture.reason' must say why the sliced-dotnet standard does not apply"));
        }

        return (new ArchitectureConfig(null, reason.Trim()), null);
    }
}

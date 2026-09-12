using System.Text.Json;

namespace Harness.Config;

internal static class WorkspaceProjects
{
    public static (IReadOnlyList<string>? Projects, string? Failure) Read(JsonElement root)
    {
        if (!root.TryGetProperty("projects", out var declared))
        {
            return ([], null);
        }

        if (declared.ValueKind != JsonValueKind.Array)
        {
            return (null, "'projects' must be an array of repository-relative directories");
        }

        var projects = new List<string>();
        foreach (var item in declared.EnumerateArray())
        {
            var path = item.ValueKind == JsonValueKind.String ? item.GetString() : null;
            if (string.IsNullOrWhiteSpace(path) || path.Any(char.IsControl) || path.IndexOfAny(['\\', ':', '*', '?', '[', ']', '\0']) >= 0
                || path.Split('/').Any(segment => segment is "" or "." or ".." || segment != segment.Trim()))
            {
                return (null, "'projects' entries must be normalized repository-relative directories without '.', '..' or empty segments");
            }

            if (projects.Any(other => path == other || path.StartsWith(other + "/", StringComparison.Ordinal)
                || other.StartsWith(path + "/", StringComparison.Ordinal)))
            {
                return (null, $"'projects' entry '{path}' duplicates or overlaps another project; nested projects are not supported");
            }

            projects.Add(path);
        }

        return (projects, null);
    }
}

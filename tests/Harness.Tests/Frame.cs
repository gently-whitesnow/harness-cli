using System.Text;
using System.Text.Json.Nodes;

namespace Harness.Tests;

/// <summary>
/// Builds a complete v4.0.0 `.harness.json` for a fixture.
/// </summary>
public sealed class Frame
{
    private const string DefaultSettings =
        """{ "adrs.shape": { "fencedBlockLineLimit": 20 }, "duplication.typescript": { "windowLines": 30, "minimumTokens": 90 }, "complexity.typescript": { "averageReachableFiles": 8.0, "largestCyclicGroupSize": 0 }, "duplication.csharp": { "windowLines": 30, "minimumTokens": 90 }, "complexity.csharp": { "averageReachableFiles": 8.0, "largestCyclicGroupSize": 0 }, "functions.csharp": { "ownTokens": 700 }, "duplication.go": { "windowLines": 30, "minimumTokens": 90 }, "complexity.go": { "averageReachableFiles": 8.0, "largestCyclicGroupSize": 0 }, "functions.go": { "ownTokens": 700 }, "verify.entrypoint": { "path": "verify.sh" }, "commits": { "language": "ru", "requireSetup": false } }""";

    private static readonly string[] TypeScriptSuffixes = [".ts", ".tsx", ".js", ".jsx", ".mts", ".cts", ".mjs", ".cjs"];

    private static readonly string[] Checks =
    [
        "harness.config",
        "harness.coverage",
        "architecture.sliced-dotnet.zone-shape",
        "architecture.sliced-dotnet.slice-shape",
        "architecture.sliced-dotnet.segment-names",
        "architecture.sliced-dotnet.layer-assemblies",
        "architecture.sliced-dotnet.dependency-direction",
        "architecture.sliced-dotnet.slice-isolation",
        "architecture.sliced-dotnet.public-api",
        "architecture.sliced-dotnet.cross-api",
        "complexity.csharp",
        "docs.policy",
        "adrs.shape",
        "commits.setup",
        "types-per-file.csharp",
        "dependencies.csharp",
        "dependencies.typescript",
        "complexity.typescript",
        "duplication.typescript",
        "duplication.csharp",
        "functions.csharp",
        "duplication.go",
        "complexity.go",
        "functions.go",
        "lint-suppressions.go",
        "lint-suppressions.ansible",
        "dependencies.ansible",
        "build-properties.dotnet",
        "central-packages.dotnet",
        "solution-format.dotnet",
        "editorconfig.dotnet",
        "warning-suppressions.dotnet",
        "verify.entrypoint",
    ];

    private readonly Dictionary<string, string> policy = Checks.ToDictionary(
        check => check,
        _ => "required",
        StringComparer.Ordinal);

    private string? settings = DefaultSettings;

    private readonly Dictionary<string, string> applicability = new(StringComparer.Ordinal)
    {
        ["csharp"] = """{ "applicable": true }""",
        ["dotnet"] = """{ "applicable": true }""",
        ["typescript"] = """{ "applicable": true }""",
        ["go"] = """{ "applicable": true }""",
        ["ansible"] = """{ "applicable": true }""",
    };
    private readonly HashSet<string> autoDeclined = new(StringComparer.Ordinal);

    private string version = Quote(Release.Current);

    private string architecture = """{ "applicable": false, "reason": "standalone fixture repository" }""";
    private string? generated;

    public static Frame Answering() => new();

    public static Frame AllPresent() => new();

    public Frame Policy(string selector, string value)
    {
        if (selector == "architecture.sliced-dotnet")
        {
            foreach (var check in Checks.Where(check => check.StartsWith(selector + ".", StringComparison.Ordinal)))
            {
                policy[check] = value;
            }
            return this;
        }
        policy[selector] = value;
        return this;
    }

    public Frame NotApplicableTo(string key, string reason = "fixture does not use this stack")
    {
        applicability[key] = $$"""{ "applicable": false, "reason": {{Quote(reason)}} }""";
        return this;
    }

    public Frame Settings(string body)
    {
        var complete = JsonNode.Parse(DefaultSettings)!.AsObject();
        Merge(complete, JsonNode.Parse(body)!.AsObject());
        settings = complete.ToJsonString();
        return this;
    }

    public Frame RawSettings(string body)
    {
        settings = body;
        return this;
    }

    public Frame Architecture(string body)
    {
        architecture = body;
        return this;
    }

    public Frame Generated(string body)
    {
        generated = body;
        return this;
    }

    public void MatchFixtureSources(IEnumerable<string> paths)
    {
        var files = paths.ToList();
        foreach (var key in new[] { "csharp", "dotnet", "typescript", "go", "ansible" })
        {
            if (applicability.TryGetValue(key, out var answer)
                && answer.Contains("\"applicable\": false", StringComparison.Ordinal)
                && !autoDeclined.Contains(key))
            {
                continue;
            }

            var present = files.Any(path => key switch
            {
                "csharp" => path.EndsWith(".cs", StringComparison.OrdinalIgnoreCase),
                "dotnet" => path.EndsWith(".csproj", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".fsproj", StringComparison.OrdinalIgnoreCase)
                    || path.EndsWith(".vbproj", StringComparison.OrdinalIgnoreCase),
                "typescript" => TypeScriptSuffixes
                    .Any(suffix => path.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)),
                "go" => path.EndsWith(".go", StringComparison.OrdinalIgnoreCase),
                "ansible" => path == "ansible.cfg" || path == ".ansible-lint"
                    || path.StartsWith("roles/", StringComparison.Ordinal),
                _ => false,
            });
            if (present)
            {
                applicability[key] = """{ "applicable": true }""";
                autoDeclined.Remove(key);
            }
            else
            {
                applicability[key] = """{ "applicable": false, "reason": "fixture has no tracked sources for this axis" }""";
                autoDeclined.Add(key);
            }
        }
    }

    public Frame Version(string value)
    {
        version = Quote(value);
        return this;
    }

    public override string ToString()
    {
        var text = new StringBuilder(
            $"{{\n  \"version\": {version},\n  \"architecture\": {architecture}");

        text.Append(",\n  \"applicability\": {\n");
        text.Append(string.Join(",\n", applicability.Select(entry =>
            $"    {Quote(entry.Key)}: {entry.Value}")));
        text.Append("\n  }");

        if (settings is not null)
        {
            text.Append(",\n  \"settings\": ").Append(settings);
        }

        if (generated is not null)
        {
            text.Append(",\n  \"generated\": ").Append(generated);
        }

        text.Append(",\n  \"policy\": {\n");
        text.Append(string.Join(
            ",\n",
            policy.Select(entry => $"    {Quote(entry.Key)}: {Quote(entry.Value)}")));
        text.Append("\n  }");

        return text.Append("\n}\n").ToString();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";

    private static void Merge(JsonObject target, JsonObject source)
    {
        foreach (var (key, value) in source)
        {
            if (value is JsonObject sourceObject && target[key] is JsonObject targetObject)
            {
                Merge(targetObject, sourceObject);
            }
            else
            {
                target[key] = value?.DeepClone();
            }
        }
    }
}

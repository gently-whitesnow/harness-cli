using System.Text;
using System.Text.Json.Nodes;

namespace Harness.Tests;

/// <summary>
/// Builds a complete `.harness.json` for a fixture. Every question has a self-reported
/// answer unless a test deliberately removes or corrupts one.
/// </summary>
public sealed class Frame
{
    private const string DefaultSettings =
        """{ "comments.csharp": { "minimumCommentLines": 10, "percentageLimit": 8 }, "comments.yaml": { "minimumCommentLines": 10, "percentageLimit": 8 }, "comments.typescript": { "minimumCommentLines": 10, "percentageLimit": 8 }, "duplication.csharp": { "windowLines": 30, "minimumTokens": 90 }, "commits": { "language": "ru", "requireSetup": false } }""";

    private static readonly string[] Questions =
        ["tests.unit", "tests.integration", "tests.architecture", "format", "lint", "build", "typecheck", "verify"];

    private static readonly string[] Checks =
    [
        "harness.config", "architecture.sliced-dotnet", "complexity.csharp", "docs.policy",
        "commits.setup", "comments.csharp", "comments.yaml", "comments.typescript",
        "types-per-file.csharp", "dependencies.csharp",
        "duplication.csharp", "build-properties.dotnet", "central-packages.dotnet",
        "solution-format.dotnet", "editorconfig.dotnet", "warning-suppressions.dotnet", "frame.tests.unit", "frame.tests.integration",
        "frame.tests.architecture", "frame.format", "frame.lint", "frame.build", "frame.typecheck", "frame.verify",
    ];

    private readonly Dictionary<string, string> answers = Questions.ToDictionary(
        question => question,
        _ => """{ "present": false, "reason": "fixture owns nothing here" }""",
        StringComparer.Ordinal);

    private readonly Dictionary<string, string> policy = Checks.ToDictionary(
        check => check,
        _ => "required",
        StringComparer.Ordinal);

    // Unrelated acceptance tests opt out of clone-local setup explicitly. Tests of the
    // shipped settings profile replace this section and exercise the actual defaults.
    private string? settings = DefaultSettings;

    private readonly Dictionary<string, string> applicability = new(StringComparer.Ordinal)
    {
        ["csharp"] = """{ "applicable": true }""",
        ["dotnet"] = """{ "applicable": true }""",
        ["yaml"] = """{ "applicable": true }""",
        ["typescript"] = """{ "applicable": true }""",
    };

    private string version = Quote(Release.Current);

    private string architecture = """{ "applicable": false, "reason": "standalone fixture repository" }""";

    /// <summary>A frame that answers "no" to every question.</summary>
    public static Frame Answering() => new();

    /// <summary>A frame with a positive answer to every question.</summary>
    public static Frame AllPresent()
    {
        var frame = new Frame();
        foreach (var question in Questions)
        {
            frame.Present(question, "fixture provides it");
        }

        frame.Located("verify", "verify.sh");

        return frame;
    }

    public Frame Located(string question, params string[] paths)
        => With(question, $$"""{ "paths": [{{string.Join(", ", paths.Select(Quote))}}] }""");

    public Frame Present(string question, string reason = "no single file carries it")
        => With(question, $$"""{ "present": true, "reason": {{Quote(reason)}} }""");

    /// <summary>Answers that one question does not apply to this repository.</summary>
    public Frame NotApplicable(string question, string reason = "no stack for it")
        => With(question, $$"""{ "applicable": false, "reason": {{Quote(reason)}} }""");

    /// <summary>Leaves one question out of the frame entirely.</summary>
    public Frame Silent(string question)
    {
        answers.Remove(question);
        return this;
    }

    /// <summary>Writes one answer verbatim, including one the reader should reject.</summary>
    public Frame With(string question, string body)
    {
        answers[question] = body;
        return this;
    }

    public Frame Policy(string selector, string value)
    {
        policy[selector] = value;
        return this;
    }

    public Frame FramePolicy(string value)
    {
        foreach (var check in Checks.Where(check => check.StartsWith("frame.", StringComparison.Ordinal)))
        {
            policy[check] = value;
        }

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

    /// <summary>Pins the frame to a release, or to the moving "latest" marker.</summary>
    public Frame Version(string value)
    {
        version = Quote(value);
        return this;
    }

    public override string ToString()
    {
        var text = new StringBuilder(
            $"{{\n  \"version\": {version},\n  \"architecture\": {architecture},\n  \"answers\": {{\n");
        text.Append(string.Join(
            ",\n",
            answers.Select(entry => $"    {Quote(entry.Key)}: {entry.Value}")));
        text.Append("\n  }");

        text.Append(",\n  \"applicability\": {\n");
        text.Append(string.Join(",\n", applicability.Select(entry =>
            $"    {Quote(entry.Key)}: {entry.Value}")));
        text.Append("\n  }");

        if (settings is not null)
        {
            text.Append(",\n  \"settings\": ").Append(settings);
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

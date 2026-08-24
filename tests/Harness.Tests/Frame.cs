using System.Text;

namespace Harness.Tests;

/// <summary>
/// Builds a complete `.harness.json` for a fixture. Every question has a self-reported
/// answer unless a test deliberately removes or corrupts one.
/// </summary>
public sealed class Frame
{
    private static readonly string[] Questions =
        ["tests.unit", "tests.integration", "tests.architecture", "format", "lint", "build", "typecheck"];

    private readonly Dictionary<string, string> answers = Questions.ToDictionary(
        question => question,
        _ => """{ "present": false, "reason": "fixture owns nothing here" }""",
        StringComparer.Ordinal);

    private readonly Dictionary<string, string> policy = new(StringComparer.Ordinal);

    private readonly List<string> suppressions = [];

    private string? settings;

    private readonly Dictionary<string, string> applicability = new(StringComparer.Ordinal);

    private string version = "3";

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

    public Frame NotApplicableTo(string key, string reason = "fixture does not use this stack")
    {
        applicability[key] = $$"""{ "applicable": false, "reason": {{Quote(reason)}} }""";
        return this;
    }

    public Frame Settings(string body)
    {
        settings = body;
        return this;
    }

    public Frame Version(string value)
    {
        version = value == "latest" ? Quote(value) : value;
        return this;
    }

    public Frame Suppressing(string check, string location, string reason = "accepted for now")
    {
        suppressions.Add(
            $$"""{ "check": {{Quote(check)}}, "location": {{Quote(location)}}, "reason": {{Quote(reason)}} }""");
        return this;
    }

    public override string ToString()
    {
        var text = new StringBuilder($"{{\n  \"version\": {version},\n  \"answers\": {{\n");
        text.Append(string.Join(
            ",\n",
            answers.Select(entry => $"    {Quote(entry.Key)}: {entry.Value}")));
        text.Append("\n  }");

        if (applicability.Count > 0)
        {
            text.Append(",\n  \"applicability\": {\n");
            text.Append(string.Join(",\n", applicability.Select(entry =>
                $"    {Quote(entry.Key)}: {entry.Value}")));
            text.Append("\n  }");
        }

        if (settings is not null)
        {
            text.Append(",\n  \"settings\": ").Append(settings);
        }

        if (policy.Count > 0)
        {
            text.Append(",\n  \"policy\": {\n");
            text.Append(string.Join(
                ",\n",
                policy.Select(entry => $"    {Quote(entry.Key)}: {Quote(entry.Value)}")));
            text.Append("\n  }");
        }

        if (suppressions.Count > 0)
        {
            text.Append(",\n  \"suppress\": [\n");
            text.Append(string.Join(",\n", suppressions.Select(entry => $"    {entry}")));
            text.Append("\n  ]");
        }

        return text.Append("\n}\n").ToString();
    }

    private static string Quote(string value) => $"\"{value.Replace("\"", "\\\"", StringComparison.Ordinal)}\"";
}

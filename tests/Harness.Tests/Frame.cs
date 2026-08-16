using System.Text;

namespace Harness.Tests;

/// <summary>
/// Builds a `.harness.json` for a fixture. Tests state only the part of the frame they are
/// about; everything else stays at the honest default of a repository that owns nothing,
/// so a test never passes because an unrelated question happened to be answered.
/// </summary>
public sealed class Frame
{
    private static readonly string[] Questions =
        ["tests.unit", "tests.integration", "tests.architecture", "format", "lint", "build", "typecheck"];

    private readonly Dictionary<string, string> declarations = Questions.ToDictionary(
        question => question,
        _ => """{ "present": false, "reason": "fixture owns nothing here" }""",
        StringComparer.Ordinal);

    private readonly Dictionary<string, string> policy = new(StringComparer.Ordinal);

    private readonly List<string> suppressions = [];

    /// <summary>A frame that answers "no" to every question, which no fixture evidence refutes.</summary>
    public static Frame Answering() => new();

    /// <summary>Answers one question with a tracked address.</summary>
    public Frame At(string question, params string[] paths)
        => With(question, $$"""{ "paths": [{{string.Join(", ", paths.Select(Quote))}}] }""");

    /// <summary>Answers one question with a claim and no address.</summary>
    public Frame Claiming(string question, string reason = "no single file carries it")
        => With(question, $$"""{ "present": true, "reason": {{Quote(reason)}} }""");

    /// <summary>Declares one question inapplicable to this repository.</summary>
    public Frame NotApplicable(string question, string reason = "no stack for it")
        => With(question, $$"""{ "applicable": false, "reason": {{Quote(reason)}} }""");

    /// <summary>Leaves one question out of the frame entirely.</summary>
    public Frame Silent(string question)
    {
        declarations.Remove(question);
        return this;
    }

    /// <summary>Writes a declaration verbatim, including one the reader should reject.</summary>
    public Frame With(string question, string body)
    {
        declarations[question] = body;
        return this;
    }

    public Frame Policy(string selector, string value)
    {
        policy[selector] = value;
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
        var text = new StringBuilder("{\n  \"version\": 1,\n  \"declarations\": {\n");
        text.Append(string.Join(
            ",\n",
            declarations.Select(entry => $"    {Quote(entry.Key)}: {entry.Value}")));
        text.Append("\n  }");

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

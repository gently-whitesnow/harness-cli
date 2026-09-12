using System.Text.Json;
using System.Text.Json.Nodes;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Reads the tracked frame into a <see cref="HarnessConfig"/>. The frame is described to this
/// reader as data, so reading a repository does not depend on being able to run a check;
/// policy is read first, and every other section is expected exactly for the checks it names.
/// </summary>
internal static class HarnessConfigReader
{
    private static readonly string[] TopLevelKeys =
        ["version", "architecture", "answers", "applicability", "settings", "policy", "projects"];

    /// <summary>
    /// Reads the tracked config and validates its envelope before preserving per-answer results.
    /// An untracked config does not exist for the harness, the same as any untracked file. Every
    /// failure names what to fix; answer failures stay local, policy-breaking failures stay global.
    /// </summary>
    public static (HarnessConfig? Config, string? Failure) Load(
        IRepository repository,
        IReadOnlyList<CheckDescriptor> checks,
        HarnessConfig? workspace = null)
    {
        var entry = repository.TrackedEntries.FirstOrDefault(candidate => candidate.Path == HarnessConfig.FileName);
        if (entry is null)
        {
            return workspace is null
                ? (null, $"'{HarnessConfig.FileName}' is not tracked in this repository, so nothing about the harness frame "
                    + $"can be established.{Environment.NewLine}{HarnessConfig.Template}")
                : (null, $"'{HarnessConfig.FileName}' is not tracked in this project directory, so nothing about the project "
                    + $"frame can be established.{Environment.NewLine}{HarnessConfig.ProjectTemplate}");
        }

        if (repository.TrackedEntries.Any(candidate => candidate.Path == HarnessConfig.RetiredBudgetFileName))
        {
            return (null, $"'{HarnessConfig.RetiredBudgetFileName}' is tracked, but this contract keeps no DSM budget: "
                + "complexity.csharp compares average reachable files and largest cyclic group size with the limits declared in settings. "
                + $"Run `git rm {HarnessConfig.RetiredBudgetFileName}` and commit.");
        }

        var (text, readFailure) = repository.ReadTrackedText(entry);
        if (text is null)
        {
            return (null, readFailure!);
        }

        JsonDocument document;
        try
        {
            document = JsonDocument.Parse(text, ConfigJson.ParseOptions);
        }
        catch (JsonException exception)
        {
            return (null, $"'{HarnessConfig.FileName}' is not readable as JSON ({exception.Message}).");
        }

        using (document)
        {
            var duplicate = ConfigJson.DuplicateProperty(document.RootElement);
            if (duplicate is not null)
            {
                return (null, ConfigJson.Failure($"duplicate property '{duplicate}' leaves the frame ambiguous; keep one"));
            }

            return workspace is null
                ? Read(document.RootElement, checks)
                : ReadProject(document.RootElement, checks, workspace);
        }
    }

    private static (HarnessConfig? Config, string? Failure) ReadProject(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks,
        HarnessConfig workspace)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("the document is not a JSON object"));
        }

        foreach (var key in new[] { "version", "projects" })
        {
            if (root.TryGetProperty(key, out _))
            {
                return (null, ConfigJson.Failure($"'{key}' belongs only in the workspace root"));
            }
        }

        if (root.TryGetProperty("policy", out var policy) && policy.ValueKind == JsonValueKind.Object
            && policy.TryGetProperty("commits.setup", out _))
        {
            return (null, ConfigJson.Failure("'policy.commits.setup' belongs only in the workspace root"));
        }

        if (!root.TryGetProperty("settings", out var settings) || settings.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("'settings' must be an object"));
        }

        if (settings.TryGetProperty("commits", out _))
        {
            return (null, ConfigJson.Failure("'settings.commits' belongs only in the workspace root"));
        }

        var envelope = JsonNode.Parse(root.GetRawText(), documentOptions: ConfigJson.ParseOptions)!.AsObject();
        envelope["version"] = workspace.TracksLatest ? "latest" : workspace.Version.ToString();
        envelope["settings"]!["commits"] = new JsonObject
        {
            ["language"] = workspace.Settings.Commits.Code,
            ["requireSetup"] = workspace.Settings.Commits.RequireSetup,
        };
        using var document = JsonDocument.Parse(envelope.ToJsonString());
        return Read(document.RootElement, checks);
    }

    private static (HarnessConfig? Config, string? Failure) Read(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks)
    {
        if (root.ValueKind != JsonValueKind.Object)
        {
            return (null, ConfigJson.Failure("the document is not a JSON object"));
        }

        foreach (var property in root.EnumerateObject())
        {
            if (property.Name is "suppress" or "overrides")
            {
                return (null, ConfigJson.Failure(
                    $"'{property.Name}' was removed in harness 2.0; remove this section"));
            }

            if (!TopLevelKeys.Contains(property.Name, StringComparer.Ordinal))
            {
                return (null, ConfigJson.Failure($"'{property.Name}' is not a key this harness reads "
                    + $"(expected {string.Join(", ", TopLevelKeys)})"));
            }
        }

        var (version, tracksLatest, versionFailure) = ReadVersion(root);
        return versionFailure is not null
            ? (null, ConfigJson.Failure(versionFailure))
            : Assemble(root, checks, version, tracksLatest);
    }

    private static (HarnessConfig? Config, string? Failure) Assemble(
        JsonElement root,
        IReadOnlyList<CheckDescriptor> checks,
        HarnessVersion version,
        bool tracksLatest)
    {
        var (projects, projectsFailure) = WorkspaceProjects.Read(root);
        if (projects is null)
        {
            return (null, ConfigJson.Failure(projectsFailure!));
        }

        var (policy, policyFailure) = PolicyReader.ReadPolicy(root, checks);
        if (policy is null)
        {
            return (null, policyFailure);
        }

        var questions = checks.Where(check => check.AnswerKey is not null).ToList();
        var (answers, answerFailures, answerFailure) = FrameAnswerReader.Read(
            root,
            questions,
            questions.Where(question => policy.ContainsKey(question.Id)).ToList());
        if (answers is null)
        {
            return (null, answerFailure);
        }

        var (applicability, applicabilityFailure) = PolicyReader.ReadApplicability(root, checks, policy.Keys);
        if (applicability is null)
        {
            return (null, applicabilityFailure);
        }

        var (settings, settingsFailure) = HarnessSettingsReader.Read(root, checks, policy.Keys);
        if (settings is null)
        {
            return (null, ConfigJson.Failure(settingsFailure!));
        }

        var architectureInFrame = checks.Any(check => check.Group == "architecture.sliced-dotnet" && policy.ContainsKey(check.Id));
        var (architecture, architectureFailure) = ArchitectureConfigReader.Read(root, architectureInFrame);

        return (new HarnessConfig
        {
            Projects = projects,
            Version = version,
            TracksLatest = tracksLatest,
            Architecture = architecture,
            ArchitectureFailure = architectureFailure,
            Answers = answers,
            AnswerFailures = answerFailures!,
            Applicability = applicability,
            Settings = settings,
            Policy = policy,
        }, null);
    }

    /// <summary>The binary implements exactly the contract named by its current release.</summary>
    private static (HarnessVersion Version, bool TracksLatest, string? Failure) ReadVersion(JsonElement root)
    {
        if (!root.TryGetProperty("version", out var declared) || declared.ValueKind != JsonValueKind.String)
        {
            return (default, false, Expected);
        }

        var text = declared.GetString();
        if (string.Equals(text, "latest", StringComparison.Ordinal))
        {
            return (HarnessVersion.Current, true, null);
        }

        if (!HarnessVersion.TryParse(text, out var version))
        {
            return (default, false, Expected);
        }

        return version == HarnessVersion.Current
            ? (version, false, null)
            : (default, false, $"'version' pins harness {version}, but this binary only runs contract "
                + $"{HarnessVersion.Current}; upgrade required — run `harness upgrade`");
    }

    private static string Expected
        => $"'version' must be a harness release such as \"{HarnessVersion.Current}\", or \"latest\"";
}

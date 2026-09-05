using System.Text.Json;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Reads the tracked frame into a <see cref="HarnessConfig"/>. The frame is described to this
/// reader as data — it never reaches for the checks themselves, so reading a repository does
/// not depend on being able to run one. Kept apart from the model so a check that holds a
/// config does not also hold every reader of its sections.
/// </summary>
internal static class HarnessConfigReader
{
    private static readonly string[] TopLevelKeys =
        ["version", "architecture", "answers", "applicability", "settings", "policy"];

    /// <summary>
    /// Reads the tracked config and validates its envelope before preserving per-answer results.
    /// An untracked config does not exist for the harness, the same as any untracked file. Every
    /// failure names what to fix; answer failures stay local, policy-breaking failures stay global.
    /// </summary>
    public static (HarnessConfig? Config, string? Failure) Load(
        IRepository repository,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var entry = repository.TrackedEntries.FirstOrDefault(candidate => candidate.Path == HarnessConfig.FileName);
        if (entry is null)
        {
            return (null, $"'{HarnessConfig.FileName}' is not tracked in this repository, so nothing about the harness frame "
                + $"can be established.{Environment.NewLine}{HarnessConfig.Template}");
        }

        if (repository.TrackedEntries.Any(candidate => candidate.Path == HarnessConfig.RetiredBudgetFileName))
        {
            return (null, $"'{HarnessConfig.RetiredBudgetFileName}' is tracked, but this contract keeps no DSM budget: "
                + "complexity.csharp compares mean reach and core size with the limits built into the binary. "
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
            document = JsonDocument.Parse(text, new JsonDocumentOptions
            {
                CommentHandling = JsonCommentHandling.Skip,
                AllowTrailingCommas = true,
            });
        }
        catch (JsonException exception)
        {
            return (null, $"'{HarnessConfig.FileName}' is not readable as JSON ({exception.Message}).");
        }

        using (document)
        {
            return Read(document.RootElement, checks);
        }
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
        var (answers, answerFailures, answerFailure) = FrameAnswerReader.Read(
            root,
            checks.Where(check => check.AnswerKey is not null).ToList());
        if (answers is null)
        {
            return (null, answerFailure);
        }

        var (architecture, architectureFailure) = ArchitectureConfigReader.Read(root);

        var (applicability, applicabilityFailure) = PolicyReader.ReadApplicability(root, checks);
        if (applicability is null)
        {
            return (null, applicabilityFailure);
        }

        var (settings, settingsFailure) = HarnessSettingsReader.Read(root);
        if (settings is null)
        {
            return (null, ConfigJson.Failure(settingsFailure!));
        }

        var (policy, policyFailure) = PolicyReader.ReadPolicy(root, checks);
        if (policy is null)
        {
            return (null, policyFailure);
        }

        return (new HarnessConfig
        {
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

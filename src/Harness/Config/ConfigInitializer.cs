using System.Text;
using Harness.Commits;
using Harness.Git;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>Creates the deliberately unanswered frame an author or agent can work through.</summary>
internal static class ConfigInitializer
{
    public static (string? Path, string? Failure) Create(
        string repositoryPath,
        bool latest,
        CommitLanguage commitLanguage,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var (repository, openFailure) = GitRepository.Open(repositoryPath);
        if (repository is null)
        {
            return (null, openFailure);
        }

        var path = System.IO.Path.Combine(repository.RootPath, HarnessConfig.FileName);
        var tracked = repository.TrackedEntries.Any(entry => entry.Path == HarnessConfig.FileName);
        if (tracked || RootEntryExists(repository.RootPath))
        {
            return (null, $"Refusing to overwrite existing '{path}'. Remove it explicitly before initializing.");
        }

        var content = Render(latest, commitLanguage, checks);
        try
        {
            using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
            using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
            writer.Write(content);
        }
        catch (IOException exception)
        {
            return (null, $"Could not create '{path}' without overwriting anything: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            return (null, $"Could not create '{path}': {exception.Message}");
        }

        return (path, null);
    }

    private static bool RootEntryExists(string rootPath)
        => Directory.EnumerateFileSystemEntries(rootPath)
            .Any(path => string.Equals(
                System.IO.Path.GetFileName(path),
                HarnessConfig.FileName,
                StringComparison.Ordinal));

    private static string Render(
        bool latest,
        CommitLanguage commitLanguage,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var defaults = HarnessSettings.Default;
        var version = latest ? "latest" : HarnessVersion.Current.ToString();
        var questions = checks
            .Where(check => check.AnswerKey is not null)
            .Select(check => check.AnswerKey!)
            .ToList();

        var text = new StringBuilder();
        text.Append("{\n  \"version\": \"").Append(version).Append("\",\n  \"answers\": {\n");
        for (var index = 0; index < questions.Count; index++)
        {
            text.Append("    \"").Append(questions[index]).Append("\": {}");
            text.Append(index == questions.Count - 1 ? '\n' : ",\n");
        }

        text.Append(
            $$"""
              },
              "applicability": {},
              "settings": {
                "comments.csharp": {
                  "minimumCommentLines": {{defaults.Comments.MinimumCommentLines}},
                  "percentageLimit": {{defaults.Comments.PercentageLimit}}
                },
                "maintainability.csharp": {
                  "fileLines": {{defaults.Maintainability.FileLines}},
                  "typeLines": {{defaults.Maintainability.TypeLines}},
                  "methodLines": {{defaults.Maintainability.MethodLines}},
                  "branches": {{defaults.Maintainability.Branches}}
                },
                "cohesion.csharp": {
                  "minimumMembers": {{defaults.Cohesion.MinimumMembers}},
                  "groups": {{defaults.Cohesion.Groups}}
                },
                "duplication.csharp": {
                  "windowLines": {{defaults.Duplication.WindowLines}},
                  "minimumTokens": {{defaults.Duplication.MinimumTokens}}
                },
                "commits": {
                  "language": "{{new CommitSettings(commitLanguage, defaults.Commits.RequireSetup).Code}}",
                  "requireSetup": {{defaults.Commits.RequireSetup.ToString().ToLowerInvariant()}}
                }
              },
              "policy": {},
              "suppress": []
            }
            """);
        return text.ToString();
    }
}

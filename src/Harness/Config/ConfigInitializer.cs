using System.Text;
using Harness.Checks;
using Harness.Git;

namespace Harness.Config;

/// <summary>Creates the deliberately unanswered frame an author or agent can work through.</summary>
internal static class ConfigInitializer
{
    public static (string? Path, string? Failure) Create(
        string repositoryPath,
        bool latest,
        IReadOnlyList<IRepositoryCheck> checks)
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

        var content = Render(latest, checks);
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

    private static string Render(bool latest, IReadOnlyList<IRepositoryCheck> checks)
    {
        var defaults = HarnessSettings.Default;
        var version = latest ? "\"latest\"" : HarnessConfig.CurrentVersion.ToString();
        var questions = checks
            .Where(check => check.Group == HarnessConfig.FrameGroup)
            .Select(check => check.Id[(HarnessConfig.FrameGroup.Length + 1)..])
            .ToList();

        var text = new StringBuilder();
        text.Append("{\n  \"version\": ").Append(version).Append(",\n  \"answers\": {\n");
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
                  "branches": {{defaults.Maintainability.Branches}},
                  "constructorParameters": {{defaults.Maintainability.ConstructorParameters}},
                  "publicMembers": {{defaults.Maintainability.PublicMembers}},
                  "importFanOut": {{defaults.Maintainability.ImportFanOut}}
                }
              },
              "policy": {},
              "suppress": []
            }
            """);
        return text.ToString();
    }
}

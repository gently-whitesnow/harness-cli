using System.Text;
using Harness.Contracts;
using Harness.Languages;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>Creates the deliberately unanswered frame an author or agent can work through.</summary>
internal static class ConfigInitializer
{
    public static (string? Path, string? EditorConfigPath, string? Failure) Create(
        IRepository repository,
        bool latest,
        CommitLanguage commitLanguage,
        RepositoryKind repositoryKind,
        IReadOnlyList<CheckDescriptor> checks,
        string initialBudget)
    {
        var path = System.IO.Path.Combine(repository.RootPath, HarnessConfig.FileName);
        var budgetPath = System.IO.Path.Combine(repository.RootPath, ".harness.budget.json");
        var tracked = repository.TrackedEntries.Any(entry => entry.Path == HarnessConfig.FileName);
        if (tracked || RootEntryExists(repository.RootPath, HarnessConfig.FileName))
        {
            return (null, null, $"Refusing to overwrite existing '{path}'. Remove it explicitly before initializing.");
        }

        var budgetTracked = repository.TrackedEntries.Any(entry => entry.Path == ".harness.budget.json");
        if (budgetTracked || RootEntryExists(repository.RootPath, ".harness.budget.json"))
        {
            return (null, null, $"Refusing to overwrite existing '{budgetPath}'. Remove it explicitly before initializing.");
        }

        // An existing .editorconfig is the repository's own answer and is kept; the reference
        // file is offered only where there is none at the root, tracked or not.
        var editorConfigPath = System.IO.Path.Combine(repository.RootPath, EditorConfigTemplate.FileName);
        var writeEditorConfig = !repository.TrackedEntries.Any(entry => entry.Path == EditorConfigTemplate.FileName)
            && !RootEntryExists(repository.RootPath, EditorConfigTemplate.FileName);

        var content = Render(latest, commitLanguage, repositoryKind, checks);
        var created = new List<string>();
        try
        {
            WriteNew(budgetPath, initialBudget);
            created.Add(budgetPath);
            WriteNew(path, content);
            created.Add(path);
            if (writeEditorConfig)
            {
                WriteNew(editorConfigPath, EditorConfigTemplate.Text);
            }
        }
        catch (IOException exception)
        {
            DeleteCreated(created);
            return (null, null, $"Could not create '{path}' without overwriting anything: {exception.Message}");
        }
        catch (UnauthorizedAccessException exception)
        {
            DeleteCreated(created);
            return (null, null, $"Could not create '{path}': {exception.Message}");
        }

        return (path, writeEditorConfig ? editorConfigPath : null, null);
    }

    private static bool RootEntryExists(string rootPath, string fileName)
        => Directory.EnumerateFileSystemEntries(rootPath)
            .Any(path => string.Equals(
                System.IO.Path.GetFileName(path),
                fileName,
                StringComparison.Ordinal));

    private static void DeleteCreated(IEnumerable<string> paths)
    {
        foreach (var created in paths)
        {
            try
            {
                File.Delete(created);
            }
            catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
            {
                // Preserve the creation failure; rollback only removes files this call created.
            }
        }
    }

    private static void WriteNew(string path, string content)
    {
        using var stream = new FileStream(path, FileMode.CreateNew, FileAccess.Write, FileShare.None);
        using var writer = new StreamWriter(stream, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
        writer.Write(content);
    }

    private static string Render(
        bool latest,
        CommitLanguage commitLanguage,
        RepositoryKind repositoryKind,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var defaults = HarnessSettings.Default;
        var version = latest ? "latest" : HarnessVersion.Current.ToString();
        var questions = checks
            .Where(check => check.AnswerKey is not null)
            .Select(check => check.AnswerKey!)
            .ToList();

        var text = new StringBuilder();
        var architecture = repositoryKind == RepositoryKind.Application
            ? "{ \"standard\": \"sliced-dotnet/1\" }"
            : "{ \"applicable\": false, \"reason\": \"standalone library\" }";
        text.Append("{\n  \"version\": \"").Append(version)
            .Append("\",\n  \"architecture\": ").Append(architecture).Append(",\n  \"answers\": {\n");
        for (var index = 0; index < questions.Count; index++)
        {
            text.Append("    \"").Append(questions[index]).Append("\": {}");
            text.Append(index == questions.Count - 1 ? '\n' : ",\n");
        }

        text.Append("  },\n  \"applicability\": {\n");
        text.Append(string.Join(",\n", checks
            .Select(check => check.Applicability)
            .Where(axis => axis is not null)
            .Distinct(StringComparer.Ordinal)
            .Select(axis => $"    \"{axis}\": {{ \"applicable\": true }}")));
        text.Append("\n  },\n  \"settings\": {\n");
        foreach (var language in Language.All)
        {
            var comments = defaults.CommentsFor(language);
            text.Append(
                $$"""
                    "{{language.Qualify("comments")}}": {
                      "minimumCommentLines": {{comments.MinimumCommentLines}},
                      "percentageLimit": {{comments.PercentageLimit}}
                    },

                """);
        }

        text.Append(
            $$"""
                "duplication.csharp": {
                  "windowLines": {{defaults.Duplication.WindowLines}},
                  "minimumTokens": {{defaults.Duplication.MinimumTokens}}
                },
                "commits": {
                  "language": "{{new CommitSettings(commitLanguage, defaults.Commits.RequireSetup).Code}}",
                  "requireSetup": {{defaults.Commits.RequireSetup.ToString().ToLowerInvariant()}}
                }
              },
              "policy": {
            """);
        text.Append('\n');
        for (var index = 0; index < checks.Count; index++)
        {
            var check = checks[index];
            var policy = check.Id.StartsWith("frame.", StringComparison.Ordinal)
                ? "off"
                : "required";
            text.Append("    \"").Append(check.Id).Append("\": \"").Append(policy).Append('"')
                .Append(index == checks.Count - 1 ? '\n' : ",\n");
        }

        text.Append("  }\n}\n");
        return text.ToString();
    }
}

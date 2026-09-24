using System.Text;
using Harness.Contracts;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Creates the deliberately unanswered frame an author or agent can work through. It names
/// only what the index shows: an axis without tracked sources gets no entry, so the file
/// says what this repository runs and nothing about stacks it does not have.
/// </summary>
internal static class ConfigInitializer
{
    public static (string? Path, string? EditorConfigPath, string? Failure) Create(
        IRepository repository,
        bool latest,
        CommitLanguage commitLanguage,
        RepositoryKind? repositoryKind,
        IReadOnlyList<FrameAxis> axes,
        IReadOnlyList<CheckDescriptor> checks)
    {
        var path = System.IO.Path.Combine(repository.RootPath, HarnessConfig.FileName);
        var tracked = repository.TrackedEntries.Any(entry => entry.Path == HarnessConfig.FileName);
        if (tracked || RootEntryExists(repository.RootPath, HarnessConfig.FileName))
        {
            return (null, null, $"Refusing to overwrite existing '{path}'. Remove it explicitly before initializing.");
        }

        // An existing .editorconfig is the repository's own answer and is kept; the reference
        // file is offered only where there is none at the root, tracked or not, and only to a
        // repository with .NET projects to hold to it.
        var editorConfigPath = System.IO.Path.Combine(repository.RootPath, EditorConfigTemplate.FileName);
        var writeEditorConfig = axes.Any(axis => axis.Key == FrameAxis.DotNet.Key)
            && !repository.TrackedEntries.Any(entry => entry.Path == EditorConfigTemplate.FileName)
            && !RootEntryExists(repository.RootPath, EditorConfigTemplate.FileName);

        var content = Render(latest, commitLanguage, repositoryKind, axes, checks, writeEditorConfig);
        var created = new List<string>();
        try
        {
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

    /// <summary>Whether the architecture standard has a subject: sliced-dotnet/1 shapes C# code.</summary>
    public static bool AsksArchitecture(IReadOnlyList<FrameAxis> axes)
        => axes.Any(axis => axis.Key == Languages.Language.CSharp.Key);

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

    /// <summary>The checks `init` puts in the frame: axis-free ones, and those of the detected axes.</summary>
    public static List<CheckDescriptor> InFrame(
        IReadOnlyList<CheckDescriptor> checks,
        IReadOnlyList<FrameAxis> axes,
        bool architecture)
        => checks
            .Where(check => check.Applicability is null
                ? architecture || check.Group != "architecture.sliced-dotnet"
                : axes.Any(axis => axis.Key == check.Applicability))
            .ToList();

    private static string Render(
        bool latest,
        CommitLanguage commitLanguage,
        RepositoryKind? repositoryKind,
        IReadOnlyList<FrameAxis> axes,
        IReadOnlyList<CheckDescriptor> checks,
        bool writesReferenceEditorConfig)
    {
        var version = latest ? "latest" : HarnessVersion.Current.ToString();
        var architecture = AsksArchitecture(axes);
        var inFrame = InFrame(checks, axes, architecture);
        var questions = inFrame.Where(check => check.AnswerKey is not null).Select(check => check.AnswerKey!).ToList();

        var text = new StringBuilder();
        text.Append("{\n  \"version\": \"").Append(version).Append("\",\n");
        if (architecture)
        {
            text.Append("  \"architecture\": ")
                .Append(repositoryKind == RepositoryKind.StandaloneLibrary
                    ? "{ \"applicable\": false, \"reason\": \"standalone library\" }"
                    : "{ \"standard\": \"sliced-dotnet/1\" }")
                .Append(",\n");
        }

        text.Append("  \"answers\": {\n");
        text.Append(string.Join(",\n", questions.Select(question => $"    \"{question}\": {{}}")));
        text.Append("\n  },\n");

        if (axes.Count > 0)
        {
            text.Append("  \"applicability\": {\n");
            text.Append(string.Join(",\n", axes.Select(axis => "    " + FrameSections.ApplicabilityEntry(axis))));
            text.Append("\n  },\n");
        }

        var sections = inFrame
            .Select(check => FrameSections.DefaultSettings(check))
            .Where(section => section is not null)
            .Concat(writesReferenceEditorConfig
                ? ["\"warning-suppressions.dotnet\": { \"repositoryWide\": [ { \"id\": \"CA1707\", \"reason\": \"sentence-style test names use underscores\" } ] }"]
                : [])
            .Append(FrameSections.CommitsSettings(new CommitSettings(commitLanguage, CommitSettings.Default.RequireSetup)))
            .ToList();
        text.Append("  \"settings\": {\n");
        text.Append(FrameSections.Indent(string.Join(",\n", sections!), "    "));
        text.Append("\n  },\n  \"policy\": {\n");
        text.Append(string.Join(",\n", inFrame.Select(check => "    " + FrameSections.PolicyEntry(check))));
        text.Append("\n  }\n}\n");
        return text.ToString();
    }
}

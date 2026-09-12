using Harness.Repository;

namespace Harness.Checks.DotNet;

internal static class EditorConfigChain
{
    public static (IReadOnlyList<EditorConfigFile>? Files, string? Failure) ReadAll(CheckContext context, EvidenceFile evidence)
    {
        var files = new List<EditorConfigFile>();
        foreach (var entry in context.Tracked(evidence).OrderBy(entry => entry.Path.Length))
        {
            var (file, failure) = EditorConfigFile.Read(context.Repository, entry);
            if (file is null)
            {
                return (null, failure);
            }

            files.Add(file);
        }

        return (files, null);
    }

    /// <summary>The files above one project, outermost first, cut at the nearest `root = true`.</summary>
    public static List<EditorConfigFile> ChainFor(IReadOnlyList<EditorConfigFile> files, string projectPath)
    {
        var directory = DirectoryOf(projectPath);
        var chain = new List<EditorConfigFile>();
        while (true)
        {
            var file = files.FirstOrDefault(candidate => candidate.Directory == directory);
            if (file is not null)
            {
                chain.Insert(0, file);
                if (file.IsRoot)
                {
                    break;
                }
            }

            if (directory.Length == 0)
            {
                break;
            }

            directory = DirectoryOf(directory);
        }

        if (!chain.Any(file => file.IsRoot))
        {
            foreach (var ancestor in files.Where(file => file.Path.StartsWith("../", StringComparison.Ordinal))
                .OrderBy(file => file.Path.Length))
            {
                chain.Insert(0, ancestor);
                if (ancestor.IsRoot)
                {
                    break;
                }
            }
        }

        return chain;
    }

    public static string RelativeTo(string root, string directory, string path)
        => directory.Length == 0 ? path : System.IO.Path.GetRelativePath(
            System.IO.Path.GetFullPath(System.IO.Path.Combine(root, directory)),
            System.IO.Path.GetFullPath(System.IO.Path.Combine(root, path))).Replace('\\', '/');

    public static string DirectoryOf(string path)
    {
        var slash = path.LastIndexOf('/');
        return slash < 0 ? string.Empty : path[..slash];
    }
}

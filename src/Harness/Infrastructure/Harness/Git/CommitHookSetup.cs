using System.Text;
using Harness.Repository;
using Harness.Versioning;

namespace Harness.Git;

/// <summary>
/// Installs and inspects the clone-local commit integration. The managed hook is the same text
/// everywhere and resolves the harness when it runs, so no binary path decides it (ADR-0052).
/// </summary>
internal sealed class CommitHookSetup : ICommitIntegration
{
    private const string Marker = "# Managed by Harness CLI.";

    private const string Shebang = "#!/bin/sh\n";

    private const string InstallForClone =
        "curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh"
        + " | sh -s -- --scope clone";

    public (CommitHookStatus? Status, string? Failure) Inspect(
        IRepository repository,
        CommitSettings settings,
        string template,
        HarnessVersion? pin)
    {
        var (paths, pathFailure) = ResolvePaths(repository.RootPath);
        if (paths is null)
        {
            return (null, pathFailure);
        }

        var (settingsStatus, settingsFailure) = InspectSettings(repository.RootPath, paths);
        if (settingsStatus is not null || settingsFailure is not null)
        {
            return (settingsStatus, settingsFailure);
        }

        var hookProblem = InspectManagedFile(paths.HookPath, HookContent(), "commit-msg hook")
            ?? InspectManagedFile(paths.TemplatePath, TemplateContent(template), $"{settings.Code} commit template")
            ?? NotExecutable(paths.HookPath);
        if (hookProblem is not null)
        {
            return (new CommitHookStatus(false, hookProblem), null);
        }

        return (InspectBinary(paths, pin), null);
    }

    public (CommitHookStatus? Status, string? Failure) Install(
        IRepository repository,
        CommitSettings settings,
        string template,
        HarnessVersion? pin)
    {
        var (paths, pathFailure) = ResolvePaths(repository.RootPath);
        if (paths is null)
        {
            return (null, pathFailure);
        }

        var conflict = ExistingConfigConflict(repository.RootPath, "core.hooksPath", paths.HooksDirectory)
            ?? ExistingConfigConflict(repository.RootPath, "commit.template", paths.TemplatePath);
        if (conflict is not null)
        {
            return (null, conflict);
        }

        try
        {
            Directory.CreateDirectory(paths.HooksDirectory);
            WriteManaged(paths.HookPath, HookContent());
            WriteManaged(paths.TemplatePath, TemplateContent(template));
            if (!OperatingSystem.IsWindows())
            {
                File.SetUnixFileMode(
                    paths.HookPath,
                    UnixFileMode.UserRead | UnixFileMode.UserWrite | UnixFileMode.UserExecute
                    | UnixFileMode.GroupRead | UnixFileMode.GroupExecute
                    | UnixFileMode.OtherRead | UnixFileMode.OtherExecute);
            }
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (null, $"Could not install commit integration: {exception.Message}");
        }

        var configFailure = WriteConfig(repository.RootPath, "core.hooksPath", paths.HooksDirectory)
            ?? WriteConfig(repository.RootPath, "commit.template", paths.TemplatePath);
        return configFailure is null
            ? Inspect(repository, settings, template, pin)
            : (null, configFailure);
    }

    private static (CommitHookStatus? Status, string? Failure) InspectSettings(string rootPath, HookPaths paths)
    {
        var (hooksPath, hooksFailure) = ReadConfig(rootPath, "core.hooksPath");
        if (hooksFailure is not null)
        {
            return (null, hooksFailure);
        }

        if (!SamePath(hooksPath, paths.HooksDirectory))
        {
            return (new CommitHookStatus(
                false,
                $"core.hooksPath is not configured for this clone: expected '{paths.HooksDirectory}', "
                + Found(hooksPath)), null);
        }

        var (templatePath, templateFailure) = ReadConfig(rootPath, "commit.template");
        if (templateFailure is not null)
        {
            return (null, templateFailure);
        }

        return SamePath(templatePath, paths.TemplatePath)
            ? (null, null)
            : (new CommitHookStatus(
                false,
                $"commit.template is not configured for this clone: expected '{paths.TemplatePath}', "
                + Found(templatePath)), null);
    }

    /// <summary>
    /// Names what is wrong with a managed file instead of calling every difference stale: missing,
    /// unmanaged, an older release's baked path, or content this release no longer writes.
    /// </summary>
    private static string? InspectManagedFile(string path, string expected, string what)
    {
        if (!File.Exists(path))
        {
            return $"the managed {what} is missing at '{path}'";
        }

        var actual = File.ReadAllText(path);
        if (string.Equals(actual, expected, StringComparison.Ordinal))
        {
            return null;
        }

        if (!IsManaged(actual))
        {
            return $"'{path}' is not managed by the harness, so the {what} was not installed; "
                + "move that file aside first";
        }

        var baked = BakedExecutable(actual);
        if (baked is null)
        {
            return $"the managed {what} at '{path}' was written by a different harness release";
        }

        var gone = File.Exists(baked) ? string.Empty : ", which no longer exists";
        return $"the {what} at '{path}' was written by an older harness and runs the fixed binary "
            + $"'{baked}'{gone}";
    }

    private static string? NotExecutable(string hookPath)
        => !OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(hookPath) & UnixFileMode.UserExecute) == 0
                ? $"the managed commit-msg hook at '{hookPath}' is not executable"
                : null;

    /// <summary>
    /// The hook is only a gate when it finds a harness at commit time, and it fails closed when it
    /// does not; a release other than the pinned one is named here, not inside the commit.
    /// </summary>
    private static CommitHookStatus InspectBinary(HookPaths paths, HarnessVersion? pin)
    {
        var binary = HookBinary.Resolve(paths.GitDirectory);
        if (binary.Path is null)
        {
            return new CommitHookStatus(
                false,
                $"the commit-msg hook cannot resolve a harness: '{binary.CloneLocalPath}' is missing and "
                + $"'harness' is not on PATH; install it into this clone with `{InstallForClone}`");
        }

        var (release, releaseFailure) = binary.Release();
        if (release is null)
        {
            return new CommitHookStatus(false, $"the harness the commit-msg hook runs is unusable: {releaseFailure}");
        }

        if (pin is not null && release != pin)
        {
            return new CommitHookStatus(
                false,
                $"the commit-msg hook runs harness {release} from '{binary.Path}', while .harness.json pins "
                + $"{pin}; install the pinned release, or `{InstallForClone}` for this clone alone");
        }

        return new CommitHookStatus(
            true,
            "commit template and commit-msg hook are active for this clone; the hook runs harness "
            + $"{release} from '{binary.Path}'");
    }

    private static string Found(string? value)
        => value is null ? "found no local setting" : $"found '{value}'";

    private static string? ExistingConfigConflict(string rootPath, string key, string expected)
    {
        var (existing, failure) = ReadConfig(rootPath, key);
        if (failure is not null)
        {
            return failure;
        }

        return existing is null || SamePath(existing, expected)
            ? null
            : $"Refusing to replace local Git setting '{key}={existing}'. Remove or reconcile it explicitly.";
    }

    /// <summary>
    /// Where the managed hook and template live: the common metadata directory, not the
    /// private one a linked worktree also has. `core.hooksPath` is a shared setting, so a
    /// worktree is the same clone for this purpose, and one setup serves all of them.
    /// </summary>
    private static (HookPaths? Paths, string? Failure) ResolvePaths(string rootPath)
    {
        var result = RunGit(rootPath, ["rev-parse", "--git-common-dir"]);
        if (result.Failure is not null || result.ExitCode != 0)
        {
            return (null, result.Failure ?? "Git did not report its metadata directory.");
        }

        var gitDirectory = Path.GetFullPath(Path.Combine(rootPath, result.StandardOutput.Trim()));
        var hooksDirectory = Path.Combine(gitDirectory, "harness-hooks");
        return (new HookPaths(
            gitDirectory,
            hooksDirectory,
            Path.Combine(hooksDirectory, "commit-msg"),
            Path.Combine(hooksDirectory, "commit-template.txt")), null);
    }

    private static (string? Value, string? Failure) ReadConfig(string rootPath, string key)
    {
        var result = RunGit(rootPath, ["config", "--local", "--get", key]);
        if (result.Failure is not null)
        {
            return (null, result.Failure);
        }

        return result.ExitCode switch
        {
            0 => (result.StandardOutput.Trim(), null),
            1 => (null, null),
            _ => (null, $"Could not read local Git setting '{key}': {result.StandardError.Trim()}"),
        };
    }

    private static string? WriteConfig(string rootPath, string key, string value)
    {
        var result = RunGit(rootPath, ["config", "--local", key, value]);
        return result.Failure ?? (result.ExitCode == 0
            ? null
            : $"Could not set local Git setting '{key}': {result.StandardError.Trim()}");
    }

    private static GitCommandResult RunGit(string rootPath, IReadOnlyList<string> arguments)
        => GitCommand.Run(arguments, rootPath);

    private static void WriteManaged(string path, string content)
    {
        if (File.Exists(path) && !IsManaged(File.ReadAllText(path)))
        {
            throw new IOException($"refusing to overwrite unmanaged '{path}'");
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    /// <summary>
    /// The marker, not the content, decides ownership: an older release's hook is still this
    /// harness's file, while an unrelated file is never touched.
    /// </summary>
    private static bool IsManaged(string content)
        => content.StartsWith(Marker, StringComparison.Ordinal)
            || content.StartsWith(Shebang + Marker, StringComparison.Ordinal);

    /// <summary>Reads the absolute path a pre-2.14 hook baked into its `exec` line, if this is one.</summary>
    private static string? BakedExecutable(string content)
    {
        const string prefix = "exec '";
        var start = content.IndexOf(prefix, StringComparison.Ordinal);
        if (start < 0)
        {
            return null;
        }

        start += prefix.Length;
        var end = content.IndexOf("' commit-message", start, StringComparison.Ordinal);
        return end < 0 ? null : content[start..end].Replace("'\"'\"'", "'", StringComparison.Ordinal);
    }

    /// <summary>The hook every clone gets, byte for byte, whatever binary writes it.</summary>
    private static string HookContent()
    {
        return Shebang + Marker + "\n"
            + """
            # The harness is resolved here, when the hook runs, so this file depends on no binary path.
            set -eu

            common_dir=$(git rev-parse --git-common-dir) || exit 1
            case "$common_dir" in
              /*) ;;
              *) common_dir=$(CDPATH= cd -- "$common_dir" && pwd -P) || exit 1 ;;
            esac

            binary="$common_dir/harness/bin/harness"
            if [ ! -x "$binary" ]; then
              binary=$(command -v harness 2>/dev/null || true)
            fi

            if [ -z "$binary" ] || [ ! -x "$binary" ]; then
              echo "harness: the commit-msg hook found no harness binary, so the commit is refused." >&2
              echo "  looked for $common_dir/harness/bin/harness and 'harness' on PATH" >&2
              echo "  install it into this clone:" >&2
              echo "    curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh -s -- --scope clone" >&2
              exit 1
            fi

            exec "$binary" commit-message check --allow-fixup "$1"

            """;
    }

    private static string TemplateContent(string template) => $"{Marker}\n{template}";

    private static bool SamePath(string? left, string right)
        => left is not null && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private sealed record HookPaths(
        string GitDirectory,
        string HooksDirectory,
        string HookPath,
        string TemplatePath);
}

using System.Diagnostics;
using System.Text;
using Harness.Commits;

namespace Harness.Git;

/// <summary>Installs and inspects the opt-in, clone-local commit message integration.</summary>
internal static class CommitHookSetup
{
    private const string Marker = "# Managed by Harness CLI.";

    public static (CommitHookStatus? Status, string? Failure) Inspect(
        GitRepository repository,
        CommitSettings settings)
    {
        var (paths, pathFailure) = ResolvePaths(repository.RootPath);
        if (paths is null)
        {
            return (null, pathFailure);
        }

        var (hooksPath, hooksFailure) = ReadConfig(repository.RootPath, "core.hooksPath");
        if (hooksFailure is not null)
        {
            return (null, hooksFailure);
        }

        if (!SamePath(hooksPath, paths.HooksDirectory))
        {
            return (new CommitHookStatus(false, "core.hooksPath is not configured for this clone"), null);
        }

        var (templatePath, templateFailure) = ReadConfig(repository.RootPath, "commit.template");
        if (templateFailure is not null)
        {
            return (null, templateFailure);
        }

        if (!SamePath(templatePath, paths.TemplatePath))
        {
            return (new CommitHookStatus(false, "commit.template is not configured for this clone"), null);
        }

        var expectedHook = HookContent(ExecutablePath());
        var expectedTemplate = TemplateContent(settings);
        if (!FileMatches(paths.HookPath, expectedHook))
        {
            return (new CommitHookStatus(false, "the managed commit-msg hook is missing or stale"), null);
        }

        if (!FileMatches(paths.TemplatePath, expectedTemplate))
        {
            return (new CommitHookStatus(false, $"the managed {settings.Code} commit template is missing or stale"), null);
        }

        if (!OperatingSystem.IsWindows()
            && (File.GetUnixFileMode(paths.HookPath) & UnixFileMode.UserExecute) == 0)
        {
            return (new CommitHookStatus(false, "the managed commit-msg hook is not executable"), null);
        }

        return (new CommitHookStatus(true, "commit template and commit-msg hook are active for this clone"), null);
    }

    public static (CommitHookStatus? Status, string? Failure) Install(
        GitRepository repository,
        CommitSettings settings)
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
            WriteManaged(paths.HookPath, HookContent(ExecutablePath()));
            WriteManaged(paths.TemplatePath, TemplateContent(settings));
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
            ? Inspect(repository, settings)
            : (null, configFailure);
    }

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

        var gitDirectory = Path.GetFullPath(Path.Combine(rootPath, result.Output.Trim()));
        var hooksDirectory = Path.Combine(gitDirectory, "harness-hooks");
        return (new HookPaths(
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
            0 => (result.Output.Trim(), null),
            1 => (null, null),
            _ => (null, $"Could not read local Git setting '{key}': {result.Error.Trim()}"),
        };
    }

    private static string? WriteConfig(string rootPath, string key, string value)
    {
        var result = RunGit(rootPath, ["config", "--local", key, value]);
        return result.Failure ?? (result.ExitCode == 0
            ? null
            : $"Could not set local Git setting '{key}': {result.Error.Trim()}");
    }

    private static GitResult RunGit(string rootPath, IReadOnlyList<string> arguments)
    {
        var startInfo = new ProcessStartInfo("git")
        {
            WorkingDirectory = rootPath,
            RedirectStandardOutput = true,
            RedirectStandardError = true,
            UseShellExecute = false,
        };
        foreach (var argument in arguments)
        {
            startInfo.ArgumentList.Add(argument);
        }

        try
        {
            using var process = Process.Start(startInfo);
            if (process is null)
            {
                return new GitResult(-1, "", "", "Could not start Git.");
            }

            var output = process.StandardOutput.ReadToEndAsync();
            var error = process.StandardError.ReadToEndAsync();
            process.WaitForExit();
            return new GitResult(process.ExitCode, output.Result, error.Result, null);
        }
        catch (Exception exception)
        {
            return new GitResult(-1, "", "", $"Could not run Git: {exception.Message}");
        }
    }

    private static void WriteManaged(string path, string content)
    {
        if (File.Exists(path))
        {
            var existing = File.ReadAllText(path);
            if (!existing.StartsWith(Marker, StringComparison.Ordinal)
                && !existing.StartsWith("#!/bin/sh\n" + Marker, StringComparison.Ordinal))
            {
                throw new IOException($"refusing to overwrite unmanaged '{path}'");
            }
        }

        File.WriteAllText(path, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
    }

    private static string ExecutablePath()
    {
        if (string.IsNullOrWhiteSpace(Environment.ProcessPath))
        {
            throw new InvalidOperationException("The harness executable path is unavailable.");
        }

        return Path.GetFullPath(Environment.ProcessPath);
    }

    private static string HookContent(string executablePath)
    {
        return $"#!/bin/sh\n{Marker}\nexec {ShellQuote(executablePath)} commit-message check --allow-fixup \"$1\"\n";
    }

    private static string TemplateContent(CommitSettings settings)
        => $"{Marker}\n{CommitTemplate.Render(settings)}";

    private static string ShellQuote(string value)
        => "'" + value.Replace("'", "'\"'\"'", StringComparison.Ordinal) + "'";

    private static bool SamePath(string? left, string right)
        => left is not null && string.Equals(
            Path.GetFullPath(left),
            Path.GetFullPath(right),
            OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal);

    private static bool FileMatches(string path, string expected)
        => File.Exists(path) && string.Equals(File.ReadAllText(path), expected, StringComparison.Ordinal);

    private sealed record HookPaths(string HooksDirectory, string HookPath, string TemplatePath);

    private sealed record GitResult(int ExitCode, string Output, string Error, string? Failure);
}

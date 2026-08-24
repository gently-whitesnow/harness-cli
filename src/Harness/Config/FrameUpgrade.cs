using System.Text;
using Harness.Git;
using Harness.Versioning;

namespace Harness.Config;

/// <summary>
/// Raises the release a repository pins. This is the only way a repository takes on checks
/// it did not run before, and it is a tracked edit: the commit that raises the pin is the
/// commit where every clone and every CI job starts running the newer contract.
/// </summary>
/// <remarks>
/// Only the pinned value is rewritten. Answers a newer release introduces are left for the
/// owner to write, the same way <see cref="ConfigInitializer"/> leaves them: the run that
/// follows names each missing key, and an invented answer would defeat the frame.
/// </remarks>
internal static class FrameUpgrade
{
    public static (string? Report, string? Failure) Raise(
        GitRepository repository,
        HarnessConfig config,
        IReadOnlyList<CheckDescriptor> checks,
        HarnessVersion target,
        bool dryRun)
    {
        if (config.TracksLatest)
        {
            return ($"{HarnessConfig.FileName} follows \"latest\", so it already runs every check this "
                + $"harness ships ({HarnessVersion.Current}). There is no pin to raise.\n", null);
        }

        if (target > HarnessVersion.Current)
        {
            return (null, $"This binary is harness {HarnessVersion.Current} and cannot pin {target}, a release "
                + "it does not ship. Update the harness first.");
        }

        if (target <= config.Version)
        {
            return ($"{HarnessConfig.FileName} already pins {config.Version}; {target} is not newer.\n", null);
        }

        var path = Path.Combine(repository.RootPath, HarnessConfig.FileName);
        var (rewritten, failure) = Rewrite(path, config.Version, target, dryRun);
        return rewritten ? (Describe(config.Version, target, checks, dryRun), null) : (null, failure);
    }

    private static string Describe(
        HarnessVersion from,
        HarnessVersion target,
        IReadOnlyList<CheckDescriptor> checks,
        bool dryRun)
    {
        var taken = checks
            .Where(check => check.Since > from && check.Since <= target)
            .ToList();

        var text = new StringBuilder();
        text.Append(dryRun ? "Would raise " : "Raised ")
            .Append(HarnessConfig.FileName)
            .Append(" from ").Append(from)
            .Append(" to ").Append(target).Append(".\n");

        if (taken.Count == 0)
        {
            text.Append("No check is introduced between these releases, so the verdict does not change.\n");
        }
        else
        {
            text.Append(dryRun ? "Checks this would take on:\n" : "Checks this takes on:\n");
            foreach (var check in taken)
            {
                text.Append("  ").Append(check.Id).Append("  introduced in ").Append(check.Since).Append('\n');
            }
        }

        text.Append(dryRun
            ? "Nothing was written.\n"
            : "Run `harness check --verbose`, then fix each new finding or record a deliberate policy for it.\n");
        return text.ToString();
    }

    /// <summary>
    /// Replaces the pinned value in place. The document is hand-maintained, so re-serializing
    /// it would rewrite formatting and drop comments the owner put there.
    /// </summary>
    private static (bool Rewritten, string? Failure) Rewrite(
        string path,
        HarnessVersion from,
        HarnessVersion target,
        bool dryRun)
    {
        string text;
        try
        {
            text = File.ReadAllText(path);
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, $"Could not read '{path}': {exception.Message}");
        }

        var current = $"\"{from}\"";
        var key = text.IndexOf("\"version\"", StringComparison.Ordinal);
        var value = key < 0 ? -1 : text.IndexOf(current, key, StringComparison.Ordinal);
        if (value < 0)
        {
            return (false, $"Could not find the pinned value {current} in '{path}'; edit 'version' by hand.");
        }

        if (dryRun)
        {
            return (true, null);
        }

        try
        {
            File.WriteAllText(path, string.Concat(text.AsSpan(0, value), $"\"{target}\"", text.AsSpan(value + current.Length)));
        }
        catch (Exception exception) when (exception is IOException or UnauthorizedAccessException)
        {
            return (false, $"Could not write '{path}': {exception.Message}");
        }

        return (true, null);
    }
}

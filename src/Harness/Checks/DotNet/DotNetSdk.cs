using Harness.Processes;

namespace Harness.Checks.DotNet;

/// <summary>
/// Whether the installed .NET SDK can be used at all. The harness never installs it: an
/// SDK it cannot invoke makes verification incomplete, never a repository violation.
/// </summary>
internal static class DotNetSdk
{
    /// <summary>
    /// The SDK localizes its output, and its components do not agree on which variable
    /// selects the language: the CLI reads DOTNET_CLI_UI_LANGUAGE, MSBuild reads VSLANG,
    /// and `dotnet format` follows the POSIX locale. The gates read that output as
    /// evidence, so all three are pinned and a finding never depends on the caller's
    /// locale.
    /// </summary>
    public static readonly IReadOnlyDictionary<string, string> InvariantOutput =
        new Dictionary<string, string>(StringComparer.Ordinal)
        {
            ["DOTNET_CLI_UI_LANGUAGE"] = "en",
            ["VSLANG"] = "1033",
            ["LANG"] = "en_US.UTF-8",
            ["LC_ALL"] = "en_US.UTF-8",
        };

    /// <summary>
    /// Every .NET gate needs the same answer and the probe costs a process. The directory
    /// is part of the key because a `global.json` there can select a different SDK, so the
    /// cache can never answer for a directory it was not asked about.
    /// </summary>
    private static readonly Dictionary<string, string?> Probed = new(StringComparer.Ordinal);

    /// <summary>Returns why the SDK is unusable, or null when it is usable.</summary>
    public static string? Verify(string workingDirectory)
    {
        if (Probed.TryGetValue(workingDirectory, out var cached))
        {
            return cached;
        }

        var run = ProcessRunner.Run("dotnet", ["--version"], workingDirectory, InvariantOutput);

        var failure = run switch
        {
            { Failure: not null } => $"The .NET SDK is required to verify this repository: {run.Failure}",
            { ExitCode: not 0 } =>
                $"The .NET SDK is required to verify this repository, but `dotnet --version` exited with "
                    + $"{run.ExitCode}. The harness does not install a toolchain.",
            _ => null,
        };

        Probed[workingDirectory] = failure;
        return failure;
    }
}

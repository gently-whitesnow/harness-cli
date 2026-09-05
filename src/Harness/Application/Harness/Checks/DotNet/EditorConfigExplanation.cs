using Harness.Config;

namespace Harness.Checks.DotNet;

internal static class EditorConfigExplanation
{
    public static readonly string Text =
        """
        Rationale
          Directory.Build.props turns code-style diagnostics into build errors, but which
          diagnostics exist is decided by .editorconfig. A repository with a short one enforces
          formatting and nothing else, and every repository writes a different short one. The
          harness carries the reviewed baseline so all of them start from the same file.

        What it reads
          Tracked SDK-style projects and the tracked .editorconfig chain above each one, resolved
          the way an editor does: nearest file last, stopping at `root = true`, a section
          applying when its glob matches a source next to the project. The toolchain is not run.

        What fails
          A project with no tracked .editorconfig above it, or a chain whose effective values for
          a C# source next to the project miss or contradict the baseline: LF, final newline,
          four-space indentation, IDE0055 formatting, sorted System-first usings outside a
          file-scoped namespace (IDE0065, IDE0161), braces everywhere (IDE0011), explicit
          accessibility (IDE0040), `var` throughout (IDE0007) and Allman braces. Each key is
          required as a warning or stricter; the naming rules of the template stay a choice.

        Remediation
          Start from the reference file `harness init` writes, or copy it below into the
          directory that holds Directory.Build.props, and keep repository-specific keys after it.
          If the repository rejects the baseline as a whole, record that decision through
          `policy.editorconfig.dotnet`.

        Reference .editorconfig
        """
        + "\n" + Indent(EditorConfigTemplate.Text) + "\n"
        + """
        Applicability
          Disable all .NET repository checks together only when they do not apply:

          "applicability": {
            "dotnet": { "applicable": false, "reason": "why .NET checks do not apply" }
          }
        """;

    private static string Indent(string text)
        => string.Join('\n', text.TrimEnd('\n').Split('\n').Select(line => line.Length == 0 ? line : "  " + line));
}

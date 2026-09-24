namespace Harness.Checks.LintSuppressions;

internal static class LintSuppressionsExplanation
{
    public const string Text =
        """
        Rationale
          `go vet` and golangci-lint hold only while nobody silences them one finding at a
          time. Suppressions accumulate quietly and each looks reasonable in its own diff. The
          harness applies its own policy rule (ADR-0035) to the linter's findings: a linter is
          either on for the repository or off for it, and no file, line or path gets a private
          exception.

        What it reads
          Tracked `.go` files, test files included. Go toolchain ignored `vendor/`,
          `testdata/`, `_` and `.` directories and `//go:build ignore` are skipped. Generated
          files are skipped only with both a declared path and toolchain marker. A
          tracked `.golangci.yml`, `.golangci.yaml`, `.golangci.toml` or `.golangci.json`. The
          configuration is read lexically: YAML by indentation and `- ` items, TOML by tables
          and `key = value`, JSON through the BCL. No linter is located, installed or run, and
          a repository without a golangci-lint configuration has nothing to report here.

        What fails
          Every `//nolint` directive blocks, including one with named linters and a reason.

          In the configuration, an exclusion rule under `issues.exclude-rules` (v1) or
          `linters.exclusions.rules` (v2) whose `path` names one place — a file, a directory,
          a suffix such as `_test\.go` — and that names `linters` or a `text` pattern:
          the same private exception, written in the config instead of the source. A path
          listed under `issues.exclude-dirs` or `issues.exclude-files` (v1) or under
          `linters.exclusions.paths` (v2) takes that place out from under every linter at
          once and is blocking the same way.

        What is printed instead
          A named linter in `linters.disable` is printed in the ordinary report when its id
          and reason appear in settings.lint-suppressions.go.repositoryWide. Blanket disables
          and exclusion rules block.

        Limits
          The readers are lexical and know the shapes golangci-lint documents use; an anchor,
          alias, merge key, flow mapping or multi-document YAML file reads as absent, not as a
          finding. A `path` is compared as text: `internal/.*` is one address, `.*` is the
          repository. `//nolint` with a space after the slashes is not a directive to
          golangci-lint and is not one here. `formatters.exclusions.paths` and
          `linters.exclusions.paths-except` (v2) are not read: the first silences formatters,
          not linters, and the second narrows a list rather than adding to it. A `//nolint`
          that a generated file or a file under `//go:build ignore` carries is skipped with
          the file.

        False positives
          A rule whose `path` names one directory on purpose — a legacy tree the owners are
          paying down — is reported at that address; the fix the check asks for is to name it
          in the source with a reason, or to switch the linter off repository-wide knowingly.

        Remediation
          Fix the code the linter points at. When the rule is wrong for this repository as a
          whole, disable it under `linters.disable` and declare its id and reason in
          `.harness.json`. If the repository rejects this
          check entirely, record that through `policy.lint-suppressions.go`.

        Applicability
          Disable every Go check together only when Go does not apply:

          "applicability": {
            "go": { "applicable": false, "reason": "why Go checks do not apply" }
          }

        Decisions
          adrs/0044-editorconfig-baseline-and-warning-suppressions.md
          adrs/0055-go-language-axis.md
        """;
}

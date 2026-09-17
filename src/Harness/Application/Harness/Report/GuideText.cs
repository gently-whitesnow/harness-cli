namespace Harness.Report;

/// <summary>
/// The working guide `harness guide` prints for an agent or a new developer in a consumer
/// repository, so that repository's AGENTS.md needs one line instead of a copy of this text.
/// </summary>
internal static class GuideText
{
    public static string For(string version) =>
        $$"""
        harness {{version}} — working guide for an agent

        What it is
          One quality frame held over many repositories. It reads only Git-tracked files
          (`git add` a new file before it counts) and never runs the repository's toolchain:
          tests, build and linters run through the script named by answers.verify.

        Reading .harness.json
          version        contract pin: a release, or "latest" to follow the installed binary.
          policy         the frame. required = a finding blocks; advisory = visible only;
                         off = decided not to run. A check the policy does not name is
                         outside the frame and does not run.
          answers        self-reported facts (tests, lint, format, build, verify): where the
                         thing lives, or { "present"/"applicable": false, "reason": "..." }.
          applicability  which language and stack axes exist in this repository.
          settings       limits of the named checks, always written in full.
          projects       optional directories with their own .harness.json frames.

        Loop
          1. harness check                          before you start and before every commit
          2. harness check --only <id> --verbose    every finding of one failing check
          3. harness explain <id>                   the rule, its evidence and the remediation
          4. Fix the code or the document, not the frame. Changing policy, settings or
             answers to make a finding disappear is the repository owner's decision: stop
             and ask. There is no per-file or per-finding suppression.
          5. Run the answers.verify script for tests, build and linters.

        Exit codes
          0  blocking checks passed; the report may still list advisory findings
          1  a blocking check proved a violation: fix it
          2  verification could not be completed (missing or invalid .harness.json, another
             contract pin, unreadable evidence): repair the setup, do not read it as a pass

        Commits
          harness setup                      once per clone: commit template and commit-msg hook
          harness commit-message template    the required message shape and language
          harness commit-message check <file>    verify a message before committing
          harness commits check <base>..<head>   what CI runs over the published range

        Upgrade
          A pin that differs from the binary ends in exit code 2. `harness upgrade --dry-run`
          prints the route, `harness upgrade` raises the pin; then run `harness check --verbose`.
          Answers and policy are never guessed: new sections are printed for the owner to decide.

        `harness help` lists every command, option and check identifier.

        """;
}

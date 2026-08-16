# Repository Quality Harness — agent navigation

Standalone CLI that checks repository quality identically for a human, an agent and
later CI. Reusable engineering rules live in executable gates, not in agent prose.

## Layout

- `src/Harness` — the CLI. NativeAOT, no installed .NET runtime required at use time.
  - `Cli/` — command line parsing, usage text, concise console report.
  - `Engine/` — gate engine: selection, ordering, timing, aggregation, exit codes.
  - `Checks/` — the shipped checks and their `explain` content.
    - `Checks/DotNet/` — .NET surface discovery and the SDK-backed format, build and test gates.
    - `Checks/Web/` — web surface discovery, package-manager selection, and the gates that
      run the repository's own format, lint, typecheck, test and build scripts.
  - `Git/` — Git evidence: tracked entries, file modes, symbolic link targets.
  - `Processes/` — external command invocation with an argument vector, never a shell.
- `tests/Harness.Tests` — acceptance tests that drive the compiled executable.
- `.scratch/harness` — temporary build specification and tickets; disposable.

## Commands

```sh
dotnet test                                        # full suite, includes NativeAOT publication
dotnet build                                       # fast feedback
dotnet publish src/Harness/Harness.csproj -c Release -r osx-arm64
./src/Harness/bin/Release/net10.0/osx-arm64/publish/harness check
```

Checking this repository with its own harness runs the whole suite through `dotnet.test`.
Use `harness check --skip dotnet.test` for the fast loop.

## Rules that matter here

- The compiled CLI process is the only test seam. Internal types are not made public
  merely to be testable.
- A check becomes blocking only when it is deterministic, actionable, fast enough for the
  normal feedback loop, low risk for false positives, and covered by a negative fixture.
  "Fast enough" bounds analysis the harness owns. A gate that delegates to the
  repository's own toolchain costs what that toolchain costs; `--skip` shortens the loop
  explicitly rather than the harness quietly running less than it reports.
- Uncertain evidence ends the run as incomplete (exit code `2`). It never becomes a pass
  and never becomes a repository violation.
- A stack the repository does not have is `not applicable`. That is distinct from a check
  that failed to execute, and it never reads as a pass.
- An execution plan is discovered from Git-tracked evidence, never configured and never
  guessed. Evidence that does not single out one plan is incomplete, not a choice. The
  caller's environment and global preferences are not repository evidence.
- A quality command the repository does not have is a readiness gap: visible in the
  report, never a pass, never a violation, and never synthesized by the harness. Gates
  run commands the repository already declares, and only ones that verify rather than fix.
- External tools localize their output. Any tool whose output is read as evidence is
  invoked with its language pinned, so findings do not depend on the caller's locale.
- The harness observes. It never edits tracked content, installs a toolchain, or changes
  a lockfile.
- NativeAOT compatibility is a build invariant: no runtime reflection, no runtime code
  generation, no dynamically loaded managed plug-ins.

## Exit codes

- `0` — every selected applicable blocking check completed and passed. Advisory findings
  and readiness gaps may still be present; the report says so instead of reading `PASS`.
- `1` — a selected applicable blocking check proved a violation.
- `2` — verification could not be completed reliably.

## Documentation policy

`ROOT.md` is the single source of agent navigation and is limited to 150 physical lines.
`AGENTS.md` and `CLAUDE.md` are direct relative symbolic links to it. `README.md` is a
short overview. Durable decisions belong under `adrs/`. Everything else is advisory.

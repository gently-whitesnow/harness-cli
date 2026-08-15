# Repository Quality Harness — agent navigation

Standalone CLI that checks repository quality identically for a human, an agent and
later CI. Reusable engineering rules live in executable gates, not in agent prose.

## Layout

- `src/Harness` — the CLI. NativeAOT, no installed .NET runtime required at use time.
  - `Cli/` — command line parsing, usage text, concise console report.
  - `Engine/` — gate engine: selection, ordering, timing, aggregation, exit codes.
  - `Checks/` — the shipped checks and their `explain` content.
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

## Rules that matter here

- The compiled CLI process is the only test seam. Internal types are not made public
  merely to be testable.
- A check becomes blocking only when it is deterministic, actionable, fast enough for the
  normal feedback loop, low risk for false positives, and covered by a negative fixture.
- Uncertain evidence ends the run as incomplete (exit code `2`). It never becomes a pass
  and never becomes a repository violation.
- The harness observes. It never edits tracked content, installs a toolchain, or changes
  a lockfile.
- NativeAOT compatibility is a build invariant: no runtime reflection, no runtime code
  generation, no dynamically loaded managed plug-ins.

## Exit codes

- `0` — every selected applicable blocking check completed and passed.
- `1` — a selected applicable blocking check proved a violation.
- `2` — verification could not be completed reliably.

## Documentation policy

`ROOT.md` is the single source of agent navigation and is limited to 150 physical lines.
`AGENTS.md` and `CLAUDE.md` are direct relative symbolic links to it. `README.md` is a
short overview. Durable decisions belong under `adrs/`. Everything else is advisory.

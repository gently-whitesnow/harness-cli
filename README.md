**English** · [Русский](README.ru.md)

# Harness CLI

A standalone CLI that holds one quality frame over many repositories. It is a deterministic
boundary between AI coding agents and a repository: the rules live in a tracked
`.harness.json`, every run reads only Git-tracked files, and the same input gives the same
verdict. Harness does not run the repository's tests, build or linters — the project's own
CI does, through a `verify` script the frame names.

Site: [harness.whitesnow.tech](https://harness.whitesnow.tech/) · Decisions: [`adrs/`](adrs/REGISTRY.md) · License: MIT

## Why

An agent will make another thousand changes to a repository, and the repository has to stay
readable and safe to change after all of them. A good agent loop is not enough: the rules
must outlive a change of agent, context window and project. Harness turns rules that are
usually re-explained by hand into checks that run after every change:

- module dependency cycles and file reachability (DSM) with explicit ceilings;
- cross-file duplication, comment density and function length;
- one short `AGENTS.md`, `CLAUDE.md` as a symlink to it, decisions in `adrs/`;
- commit message shape, checked by a `commit-msg` hook and in CI;
- a hardened .NET baseline and the `sliced-dotnet/1` architecture (layers × slices);
- self-reported answers: where the tests, lint, build and `verify` script are, or why not.

It is for maintainers who let agents change their code and want the same rules everywhere.
Languages: C#/.NET (the fullest set), Go, TypeScript/JavaScript, YAML and Ansible — 44 checks;
`harness help` lists them, `harness explain <check-id>` explains one.

## Install

```sh
curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
```

The script picks the build for macOS arm64 or Linux x64/arm64 (glibc or musl), verifies its
sha256 and puts it in `~/.local/bin/harness` without sudo. It is a NativeAOT binary: no .NET
runtime, only Git. The same command updates it and, inside a framed repository, runs `harness setup`.

`HARNESS_VERSION=3.9.0` installs a given release, `HARNESS_INSTALL_DIR` changes the directory,
`HARNESS_NO_SETUP=1` skips clone setup. `sh -s -- --scope clone` installs into the clone's
`<git-common-dir>/harness/bin/`, where the `commit-msg` hook looks before `PATH`.

## Quick start

```sh
cd /path/to/repository
harness init --kind application   # or --kind library; asked only when C# is in the index
git add .harness.json            # and .editorconfig, which init writes for .NET
harness check                     # fill in the answers until the run is complete
harness check --only <check-id> --verbose
harness explain <check-id>
```

`init` detects languages from the Git index (`--languages go,yaml` replaces detection) and
writes only their sections. Every written check starts as `required`. Answers about tests,
lint, format, build, typecheck and `verify` start empty and keep the run `Incomplete`
(exit `2`) until you answer them. `init` also activates the commit hook; after a fresh clone
run `harness setup`. `harness guide` prints the agent's working loop for a consumer `AGENTS.md`.

## Example output

A C# repository where `Orders` and `Billing` use each other and a stray `NOTES.md` is tracked
(abridged, long lines wrapped):

```text
$ harness check --only dependencies.csharp,docs.policy --verbose
FAIL  /work/shop
  harness 3.9.0 · repository pins 3.9.0
   CHECK ID             FINDINGS
❌ docs.policy                 1
    violation  NOTES.md: unexpected tracked Markdown; remove it, fold navigation into
               AGENTS.md, or record a concise decision in adrs/
❌ dependencies.csharp         1
    violation  src/Shop/Billing/Invoice.cs:7: module dependency cycle
               Shop.Billing -> Shop.Orders -> Shop.Billing: Shop.Billing.Invoice names
               Shop.Orders.Order at src/Shop/Billing/Invoice.cs:7; Shop.Orders.Order names
               Shop.Billing.Invoice at src/Shop/Orders/Order.cs:7.
```

Exit codes: `0` — every selected applicable blocking check passed (advisory findings may
remain); `1` — a blocking check proved a violation; `2` — verification could not be completed
reliably, including a missing or invalid `.harness.json`.

## The frame

The frame is explicit, like EditorConfig. [`.harness.json`](.harness.json) of this repository
is the reference and shows every form:

```jsonc
"tests.unit":        { "paths": ["tests/Unit"] }  // present; the address is the test project
"lint":              { "present": true,  "reason": "analyzers in Directory.Build.props" }
"tests.integration": { "present": false, "reason": "planned in ISSUE-142" }
"typecheck":         { "applicable": false, "reason": "no web stack" }
```

A check the `policy` does not name is outside the frame: it does not run, and the report
counts it in one line. A named check is `required` (a finding blocks), `advisory` (visible,
never blocks) or `off`, and carries its `settings` section in full — there are no hidden
defaults and no per-file or per-finding suppression. `harness.coverage` reports a language
with tracked sources that `applicability` does not mention and prints the fragment to add;
decline it with `{ "applicable": false, "reason": "..." }`. Answers are self-reported:
Harness validates their form, `paths` are navigation, and the `verify` script runs the rest.

## Monorepos

The root `.harness.json` registers disjoint directories: `"projects": ["apps/api", "apps/web"]`.
Each has its own tracked `.harness.json` with `answers`, `applicability`, `settings` and
`policy`; nothing is inherited, and only the root holds `version` and commit settings.
`harness check` covers the whole workspace from any directory; `harness check --project
apps/api` validates every frame and reports a partial run. Duplication and graphs are
measured inside each project, not across projects. [ADR-0060](adrs/0060-explicit-workspace-projects.md)

## Contract version

`version` pins one contract for the workspace (`"3.9.0"`); `"latest"` follows the installed
binary. A binary runs only its own contract, and any other pin exits with `2`.
`harness upgrade` raises the pin and prints the migration route with fragments for the
detected axes; it never guesses the owner's answers. [ADR-0023](adrs/0023-release-version-as-the-verification-contract.md)

## CI

GitLab:

```yaml
harness:
  image: ghcr.io/gently-whitesnow/harness:3.9.0
  script:
    - harness check
    - harness commits check "$CI_MERGE_REQUEST_DIFF_BASE_SHA..$CI_COMMIT_SHA"
```

GitHub Actions, or any runner without access to ghcr.io:

```yaml
- name: Repository harness
  run: |
    curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
    ~/.local/bin/harness check
```

Put `harness check` into the `verify` script next to tests, build and linters. The commit
hook tolerates a temporary autosquash; the range given to `harness commits check` does not.

## Repository

`src/Harness` follows its own `sliced-dotnet/1` standard, `tests/Harness.Tests` runs the
compiled binary, and `./verify.sh` runs the harness, format, tests and a NativeAOT publish.
`site/` mirrors the check registry and version under test ([ADR-0047](adrs/0047-landing-mirrors-the-contract.md)).

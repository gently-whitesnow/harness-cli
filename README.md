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
- cross-file duplication and whitespace-invariant function size (`ownTokens`);
- short `AGENTS.md` and `README.md` (≤ 1200 words), `CLAUDE.md` as a symlink to `AGENTS.md`, decisions in `adrs/`;
- commit message shape, checked by a `commit-msg` hook and in CI;
- a hardened .NET baseline and the `sliced-dotnet/1` architecture (layers × slices);
- `verify.entrypoint`: proves from the Git index that `verify.sh` is tracked, executable (`100755`) and invokes `harness check`.

It is for maintainers who let agents change their code and want the same rules everywhere.
Languages: C#/.NET (the fullest set), Go, TypeScript/JavaScript and Ansible — 35 checks;
`harness help` lists them, `harness explain <check-id>` explains one.

## Install

```sh
curl -fsSL https://raw.githubusercontent.com/gently-whitesnow/harness-cli/master/install.sh | sh
```

The script picks the build for macOS arm64 or Linux x64/arm64 (glibc or musl), verifies its
sha256 and puts it in `~/.local/bin/harness` without sudo. It is a NativeAOT binary: no .NET
runtime, only Git. The same command updates it and, inside a framed repository, runs `harness setup`.

`HARNESS_VERSION=4.0.0` installs a given release, `HARNESS_INSTALL_DIR` changes the directory,
`HARNESS_NO_SETUP=1` skips clone setup. `sh -s -- --scope clone` installs into the clone's
`<git-common-dir>/harness/bin/`, where the `commit-msg` hook looks before `PATH`.

## Quick start

```sh
cd /path/to/repository
harness init --kind application   # or --kind library; asked only when C# is in the index
git add .harness.json            # and .editorconfig, which init writes for .NET
harness check                     # verify AGENTS.md, CLAUDE.md symlink and executable verify.sh
harness check --only <check-id> --verbose
harness explain <check-id>
```

`init` detects languages from the Git index (`--languages go,typescript` replaces detection) and
writes only their sections. Every written check starts as `required`. `init` also activates the
commit hook; after a fresh clone run `harness setup`. `harness guide` prints the agent's working
loop for a consumer `AGENTS.md`.

## Example output

A C# repository where `Orders` and `Billing` use each other and a stray `NOTES.md` is tracked
(abridged, long lines wrapped):

```text
$ harness check --only dependencies.csharp,docs.policy --verbose
FAIL  /work/shop
  harness 4.0.0 · repository pins 4.0.0
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
is the reference. A check the `policy` does not name is outside the frame: it does not run,
and the report counts it in one line. A named check is `required` (a finding blocks), `advisory`
(visible, never blocks) or `off`, and carries its `settings` section in full — there are no
hidden defaults and no per-file or per-finding suppression. `harness.coverage` reports a
language with tracked sources that `applicability` does not mention and prints the fragment to
add; decline it with `{ "applicable": false, "reason": "..." }`.

## Monorepos

The root `.harness.json` registers disjoint directories: `"projects": ["apps/api", "apps/web"]`.
Each has its own tracked `.harness.json` with `applicability`, `settings` and `policy`; nothing
is inherited, and only the root holds `version` and commit settings. `harness check` covers the
whole workspace from any directory; `harness check --project apps/api` validates every frame and
reports a partial run. Duplication and graphs are measured inside each project, not across
projects. [ADR-0060](adrs/0060-explicit-workspace-projects.md)

## Contract version

`version` pins one contract for the workspace (`"4.0.0"`); `"latest"` follows the installed
binary. A binary runs only its own contract, and any other pin exits with `2`.
`harness upgrade` raises the pin, migrates the frame schema and prints the migration route.
[ADR-0023](adrs/0023-release-version-as-the-verification-contract.md)

## CI

GitLab:

```yaml
harness:
  image: ghcr.io/gently-whitesnow/harness:4.0.0
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

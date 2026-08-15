# Repository Quality Harness v0

**Status:** ready-for-agent  
**Lifetime:** temporary build specification; remove after the work has shipped and durable decisions have been captured in ADRs and change history.

## Problem Statement

Engineering agents are currently guided partly by prose instructions and partly by repository-local quality scripts. Prose can be skipped, misunderstood, or become stale. The reusable scripts are copied between repositories, grow into large forks, and do not have a reliable upgrade path. In the reference `services-platform` repository, this has produced more than a thousand lines of duplicated maintainability and duplication analysis alongside a larger repository-local runner.

The desired direction is for reusable engineering rules to live in executable checks instead of agent instructions. Product behavior and repository-specific architectural invariants remain in the repository as code, tests, contracts, and architecture tests. A standalone tool should own checks that can be applied consistently across repositories, run the repository's existing standard toolchain, expose gaps that it cannot prove, and eventually be suitable for the same use in CI.

The first version must create value quickly. It must not attempt to infer a repository's intended architecture, automatically fix production code, introduce a permanent specification system, optimize affected-test selection, or claim certainty that its evidence does not support.

## Solution

Build a standalone command-line quality harness for macOS and Linux. A user or engineering agent invokes one `check` command against a repository. The tool discovers supported .NET and TypeScript/Vite/React surfaces, runs applicable standard quality commands, performs reusable maintainability and duplication analysis, audits the repository's Markdown policy, detects known quality capabilities, records the duration of every gate, and reports concise actionable results.

The tool is implemented as a C# NativeAOT executable and distributed as OS- and architecture-specific self-contained binaries. The harness itself requires no installed .NET runtime. Tools needed by the repository, such as the .NET SDK, Git, Node, or pnpm, remain explicit environmental capabilities and are never installed silently.

Checks are independent, have stable identifiers, can be selected or skipped, and expose more detail through `explain`. A check becomes blocking only when it is deterministic, actionable, sufficiently fast, protected by negative tests, and has a low risk of false positives. Heuristic quality findings and uncertain capability detection remain advisory in v0.

The primary verification seam is the compiled CLI process. Tests invoke the executable against fixture repositories and assert exit status, observable output, findings, timings, selection behavior, and preservation of tracked repository content.

## User Stories

1. As an engineering agent, I want to run one command against a repository, so that I can discover quality problems without reading a collection of repository-specific scripts.
2. As a repository maintainer, I want reusable quality checks to ship inside one versioned tool, so that fixes and improvements do not have to be copied into every repository.
3. As a repository maintainer, I want product behavior and business cases to remain in repository-owned tests, so that the generic harness does not become the owner of domain knowledge.
4. As a repository maintainer, I want repository-specific architectural rules to remain in architecture tests, so that the harness does not guess what architecture the repository intended.
5. As an engineering agent, I want the harness to report that architecture checks were not detected, so that absence of evidence is visible without being misrepresented as proof that no tests exist.
6. As an engineering agent, I want unsupported or ambiguous repository structures to be reported as unknown, so that a green result is not fabricated from incomplete analysis.
7. As a .NET repository maintainer, I want the harness to discover solutions and projects, so that applicable checks can run without initial configuration.
8. As a .NET repository maintainer, I want formatting verification, build, and tests to run through standard .NET tooling, so that the result reflects the repository's real toolchain.
9. As a web repository maintainer, I want the harness to detect the package manager from repository evidence, so that it invokes commands through the correct ecosystem.
10. As a web repository maintainer, I want existing format, lint, typecheck, test, and build scripts to be discovered and run when present, so that the harness respects established project conventions.
11. As a repository maintainer, I want a missing standard quality command to appear as a readiness gap, so that missing infrastructure is not confused with a code violation.
12. As an engineering agent, I want every invoked command to be visible, so that I can reproduce a failure outside the harness.
13. As a repository maintainer, I want the harness never to install dependencies or modify a lockfile automatically, so that a quality check cannot silently change the project.
14. As a repository maintainer, I want the harness not to intentionally modify tracked source or configuration, so that checking remains observational rather than corrective.
15. As an engineering agent, I want maintainability hotspots reported with the metric, measured value, subject, and location, so that I can judge whether a refactor is appropriate.
16. As an engineering agent, I want duplication findings grouped and localized, so that I can distinguish a reusable abstraction opportunity from harmless structural similarity.
17. As a repository maintainer, I want heuristic maintainability and duplication findings to remain advisory initially, so that approximate analysis does not impose universal architectural taste.
18. As a future policy author, I want every check to have a stable identifier, so that it can later be configured, suppressed, reviewed, and enforced without depending on display text.
19. As an engineering agent, I want to run only a named check or group, so that I can shorten a local feedback loop explicitly without pretending that the full repository passed.
20. As a repository maintainer, I want skipped checks to remain visible in the result, so that disabling evidence cannot masquerade as success.
21. As an engineering agent, I want an `explain` command for a check identifier, so that normal output stays compact while unfamiliar rules remain understandable on demand.
22. As an engineering agent, I want the default output to emphasize failures, readiness gaps, skipped checks, and timings, so that limited context is spent on information that changes my next action.
23. As a repository maintainer, I want every gate's duration reported, so that future optimization is driven by measured feedback cost.
24. As a repository maintainer, I want the first version to execute the full applicable check set by default, so that premature affected-test logic cannot create false green results.
25. As a caller, I want exit code `0` to mean all active blocking checks completed and passed, so that successful execution has a stable meaning.
26. As a caller, I want exit code `1` to mean a proven blocking violation was found, so that repository defects are distinguishable from harness failures.
27. As a caller, I want exit code `2` to mean the requested verification could not be completed reliably, so that missing toolchains and analyzer faults do not look like repository violations or success.
28. As a repository maintainer, I want checks that do not apply to the discovered stack to be reported distinctly from checks that failed to execute, so that heterogeneous repositories remain understandable.
29. As a repository maintainer, I want only tracked Markdown considered by documentation policy, so that dependency caches and generated artifacts do not create noise.
30. As a repository maintainer, I want `ROOT.md` to be the single source of agent navigation and instructions, so that different agent entry points cannot drift.
31. As a repository maintainer, I want `AGENTS.md` and `CLAUDE.md` to be required direct relative symbolic links to `ROOT.md`, so that both agent ecosystems receive identical instructions.
32. As a repository maintainer, I want copied files, chained links, broken links, and links to different targets detected, so that apparent synchronization is not mistaken for actual synchronization.
33. As a repository maintainer, I want `ROOT.md`, `README.md`, `AGENTS.md`, and `CLAUDE.md` constrained to at most 150 physical lines where content exists, so that navigation remains small enough for an agent context.
34. As a repository maintainer, I want durable Markdown decisions located under the root `adrs` directory, so that architectural rationale has one discoverable home.
35. As a repository maintainer, I want other tracked Markdown reported as advisory, so that stale specifications and scattered instructions become visible without blocking the initial rollout.
36. As a repository maintainer, I want OpenAPI, YAML, schemas, and other non-Markdown contracts to remain outside the Markdown policy, so that executable product contracts are not confused with narrative specifications.
37. As a macOS user, I want to install one native executable, so that I do not need to install a runtime solely for the harness.
38. As a Linux CI operator, I want a self-contained native executable for the runner architecture, so that the same CLI can later be embedded in a pipeline.
39. As a release maintainer, I want each supported binary built and smoke-tested for its target, so that standalone distribution is an exercised behavior rather than a packaging claim.
40. As a maintainer of the harness, I want every blocking rule protected by a negative fixture, so that the harness proves it detects the prohibited state.
41. As a maintainer of the harness, I want unknown layouts and incomplete evidence covered by tests, so that new discovery logic fails honestly rather than guessing.
42. As a maintainer of the harness, I want the real `services-platform` repository used as an acceptance workload, so that the tool evolves against production-scale conditions without hard-coding that repository's layout.
43. As a maintainer of multiple repositories, I want to run the tool against additional real projects, so that reusable checks improve through empirical evidence rather than a single canonical example.
44. As a future CI adopter, I want the local command interface and exit semantics to remain suitable for CI, so that later enforcement does not require a second implementation of the rules.

## Implementation Decisions

- The executable interface is centered on `harness check` with an optional repository argument. The current directory is used when no repository is supplied.
- `check` performs repository inventory internally. There is no separate public `inspect` workflow in v0.
- The only normative external testing seam is the compiled CLI process. Internal modules remain implementation details and are not exposed merely to make tests easier.
- The harness is implemented in C# and published with NativeAOT as self-contained, target-specific binaries for macOS and Linux on x64 and arm64 where release infrastructure supports the target.
- NativeAOT compatibility is a build invariant. Runtime reflection, runtime code generation, and dynamically loaded managed plug-ins are avoided. Serialization uses AOT-compatible generation.
- Future extensions should prefer an out-of-process protocol over loading plug-in assemblies. An extension protocol is not part of v0.
- The command module presents a small interface and delegates repository discovery, planning, execution, analysis, and reporting to internal modules.
- The gate engine is a deep module: callers provide the repository and selection options; the engine owns applicability, ordering, execution, timing, aggregation, and exit semantics.
- Every check has a stable identifier, summary, applicability result, severity, execution result, duration, concise evidence, and explanation content.
- `--only` and `--skip` select independent checks or documented check groups. Skipped checks are always included in the final summary.
- Checks run without affected-file optimization or a custom task cache. Execution is conservative and deterministic; timing data guides later performance work.
- The process runner invokes executables with argument vectors rather than through an implicit shell. It records the displayed command, exit status, duration, and bounded diagnostic output.
- Applicable external commands that cannot be executed make verification incomplete and produce exit code `2`. A stack that is not present is not an execution error.
- The harness never installs a missing toolchain, restores a missing package manager globally, edits project files, changes policy, or modifies dependency lockfiles.
- Normal build and test commands may create their conventional ignored outputs. The harness itself does not apply fixes or intentionally modify tracked repository content.
- .NET discovery uses repository evidence such as solutions, project files, and standard tool manifests. The initial adapter runs verification-formatting, build, and test commands supported by the installed SDK.
- Web discovery determines the package manager from lockfiles and recognizes the existing stack used by the reference repository. It prefers existing non-mutating scripts for formatting verification, lint, typecheck, test, and build.
- A missing web script is a readiness gap, not permission for the harness to invent a repository-specific command. Mutating format scripts are not run as checks.
- The maintainability analyzer begins from the behavior of the existing copied script but is reimplemented as a tool-owned check. Its initial evidence includes logical size, approximate control-flow complexity, constructor arity, public surface, and import fan-out.
- Maintainability formulas are named precisely. Import fan-out is not presented as semantic module coupling, constructor arity is not presented as dependency count, and lexical complexity is not presented as a compiler control-flow metric.
- The duplication analyzer begins from the existing normalized cross-file window approach, while improving correctness and output where tests expose concrete defects. It does not claim semantic duplication.
- Maintainability and duplication are advisory in v0. They produce evidence and suggested investigation, not a universal blocking threshold.
- True cohesion metrics, afferent/efferent coupling, instability, abstractness, distance, general dependency-cycle analysis, and inferred clean-architecture policy are not silently folded into the initial heuristics.
- Capability detection looks for explicit, known evidence of repository-owned quality checks. Its vocabulary distinguishes detected, executed, not detected, unknown, and not applicable.
- Capability detection never converts absence of recognized evidence into the assertion that a capability does not exist. A future manifest may turn declared repository commands into explicit evidence.
- The Markdown policy considers tracked Markdown and ignores generated, vendored, and build-output locations.
- `ROOT.md` is the canonical root instruction and navigation document and is limited to 150 physical lines.
- `AGENTS.md` and `CLAUDE.md` are required Git symbolic links whose direct relative target is `ROOT.md`. Regular-file copies, chained links, broken links, absolute links, and alternate targets are findings.
- A root `README.md` is allowed as overview and navigation and is limited to 150 physical lines.
- Durable Markdown architectural decisions are allowed below a root `adrs` directory. The policy does not constrain non-Markdown executable contracts such as OpenAPI or schemas.
- Other tracked Markdown produces an advisory documentation finding in v0. The harness recommends removal, consolidation into navigation, or migration of durable rationale to an ADR; it does not edit documentation.
- Default terminal output is concise: overall result, blocking violations, advisory findings that require judgment, readiness gaps, skipped or incomplete checks, and per-gate duration.
- Detailed formulas, rationale, evidence interpretation, and remediation guidance are available through `harness explain <check-id>` rather than repeated in every run.
- Exit code `0` means every selected applicable blocking check completed and passed. Advisory findings and readiness gaps may still be present.
- Exit code `1` means at least one selected applicable blocking check completed and proved a violation.
- Exit code `2` means the requested verification was not completed reliably because of an environmental prerequisite, analyzer failure, ambiguous execution plan, or comparable tool error. Incompleteness takes precedence over a green result.
- A single numeric AI-readiness score is not produced. Readiness is represented by named capabilities with evidence and explicit uncertainty.
- The reference production repository is an acceptance workload, not a source of implicit universal policy. Layout-specific behavior requires general evidence or remains unknown.
- The temporary build specification and local tickets are disposable coordination artifacts. Durable product behavior remains in code and tests; durable technical rationale belongs in ADRs; change intent belongs in task, review, and commit history.

## Testing Decisions

- The highest and primary seam is the compiled CLI process. Tests invoke it against a repository fixture and assert externally observable behavior rather than internal class interactions.
- A good acceptance test proves a caller-visible contract: exit code, semantic output, stable check identifiers, selected checks, evidence locations, readiness status, durations being present, and preservation of tracked content.
- Output tests assert semantic records and essential text rather than complete ANSI formatting or incidental ordering that is not part of the interface.
- Small fixture repositories cover a supported .NET repository, a supported Vite/React repository, a mixed repository, an empty repository, and an unknown repository shape.
- Each blocking check has at least one passing fixture and one negative fixture that proves the prohibited state returns exit code `1`.
- Environmental failure fixtures provide controlled executables through the test process environment to prove missing commands, non-zero tool failures, timeouts if supported, and malformed output produce exit code `2` without requiring internal mocks.
- Selection tests prove that `--only` and `--skip` affect only the named checks, that skipped checks remain visible, and that selecting an unknown identifier fails clearly.
- Explanation tests prove that every shipped check identifier has concise rationale, evidence interpretation, and remediation guidance.
- Documentation fixtures use actual Git-tracked symbolic links and regular files to cover correct direct links, copies, chains, broken links, absolute targets, divergent targets, line limits, allowed ADRs, ignored generated content, and unexpected Markdown.
- .NET integration fixtures run real SDK commands through the CLI when the SDK is part of the test environment. Tests prove command discovery and exit translation, not the internal implementation of the SDK.
- Web integration fixtures run real package-manager scripts through the CLI when the package manager and dependencies are available. Tests prove lockfile selection, non-mutating script selection, missing-script readiness, and failure translation.
- Repository fixtures are isolated. After a check, tests compare tracked content and Git state to prove the harness did not apply source, policy, or lockfile changes. Conventional ignored build outputs are permitted.
- Maintainability tests preserve intentional compatibility with useful behavior from the existing script and add focused negative cases for lexical edge conditions, exclusions, line attribution, thresholds when displayed, and misleading metric names.
- Duplication tests cover normalized clones, adjacent-window coalescing where implemented, cross-file behavior, identifier and literal normalization, exclusions, and unrelated structures that must not be overstated as semantic duplication.
- Heuristic analyzer tests verify measured facts and explanation quality. They do not encode a disputed universal refactoring decision as a blocking assertion.
- Capability tests cover recognized architecture and integration-test evidence, no evidence, ambiguous evidence, an executable capability, and a newly added project that falls outside an explicitly enumerated repository-owned check where that gap can be observed.
- Timing tests assert that every attempted gate reports a non-negative duration; they do not assert exact wall-clock thresholds on shared infrastructure.
- NativeAOT publication is tested by building release artifacts with zero AOT warnings and smoke-running each distributed artifact on a matching supported runner where available.
- The real `services-platform` repository is exercised as a higher-level acceptance workload. Assertions target discovered capabilities, command planning, honest uncertainty, and bounded output rather than hard-coded paths or exact test counts.
- Additional real repositories are used during development to find false assumptions. A behavior becomes a universal blocking rule only after negative evidence and tests justify it.

## Out of Scope

- Automatic repair or source-code refactoring.
- An `init` command or generated repository-local scripts.
- A repository manifest, per-file exceptions, suppressions, expiry, or waiver governance.
- CI pipeline templates, merge protection, or enforcement configuration in consuming repositories.
- Automatic affected-project or affected-test selection.
- Local or remote task-result caching.
- Parallel execution as a performance feature.
- A scalar AI-readiness or quality score.
- Automatic inference or enforcement of Clean Architecture, feature-sliced design, domain boundaries, or repository-specific dependency directions.
- Generation, validation-depth certification, or mutation testing of repository-owned architecture tests.
- A general rule-definition DSL or policy engine.
- In-process plug-in loading or a public extension protocol.
- True module cohesion, Ca/Ce, instability, abstractness, distance-from-main-sequence, or general semantic dependency-cycle metrics.
- Universal blocking thresholds for LOC, complexity, fan-out, constructor arity, public surface, or duplication.
- Persistent synchronization of feature specifications with code.
- JSON, SARIF, JUnit, hosted dashboards, or remote telemetry unless a concrete first consumer makes one necessary.
- Windows distribution in v0.
- Automatic installation of Git, the .NET SDK, Node, package managers, or repository dependencies.
- Automatic modification or deletion of Markdown documentation.

## Further Notes

- The tool's governing principle is that executable checks, not agent prose, carry reusable enforceable engineering rules. Text remains valid for navigation, explanations, temporary task intent, and durable rationale that cannot be recovered from a gate.
- Code and tests describe current behavior. Executable contracts describe public interfaces. ADRs explain durable, expensive, or difficult-to-reverse decisions. Tasks, merge requests, and commits preserve change intent and history.
- The current production harness demonstrates useful gates but also demonstrates the cost of copy-and-fork scripts, prose-only prohibitions, hard-coded inventories, and checks that can be skipped without an enforcement boundary.
- v0 is a local assistant rather than a security boundary. It helps a cooperative agent detect and repair machine-observable defects. Protected CI enforcement is a future use of the same command and exit contract.
- New blocking checks must be admitted conservatively. A check must be deterministic, low-noise, explain concrete harm, suggest a localized response, execute within the normal feedback loop, and include a negative test proving that it detects the prohibited state.
- Rules remain independently selectable because engineering judgment can be wrong. In v0, disabling a check is explicit and visible but does not require persistent rationale. Future manifest and CI work may make exceptions reviewable and governed.
- Feedback performance is measured before it is optimized. The initial full run establishes the cost distribution needed to decide whether affected execution, caching, parallelism, or fast/deep profiles are worthwhile.

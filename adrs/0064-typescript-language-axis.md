# ADR-0064: TypeScript and JavaScript structural axis

## Status

Accepted

## Context

The TS/JS axis previously measured only comment density. Frontend repositories can have module cycles, duplicated blocks, and broad transitive reach. The shared duplication and graph checks already support another language reader. Issue #58 records a six-repository pilot: transparent barrels reduced measured reach substantially, while directory cycles remained visible. TypeScript import cycles are allowed by the compiler.

## Decision

Add `dependencies.typescript`, `duplication.typescript`, and `complexity.typescript` to contract 3.6. Literal imports and reexports resolve against tracked source, tsconfig and package manifests without Node or tsc. Unresolved imports are reported without inventing graph edges. Tests and stories are excluded from the graph but included in duplication. Reexport-only barrels are transparent in the DSM. Directory modules define dependency cycles; a file is the DSM node. The initial duplication window is 30 lines / 90 tokens. The initial DSM ceiling is 12.0 average reachable files and zero cyclic group.

## Consequences

### Positive

- Frontend coupling and repetition become visible under the same explicit policy as C# and Go.
- Transparent barrels make average reach describe definitions rather than index-file layout.

### Negative / Risks

- Lexical resolution cannot model arbitrary runtime module loaders or untracked package configuration. Missing edges can understate connectivity; the report names unresolved imports and raises `Incomplete` when a broken configuration coincides with unresolved bare specifiers.

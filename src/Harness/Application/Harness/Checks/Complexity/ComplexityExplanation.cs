using System.Globalization;
using Harness.Config;
using Harness.Languages;

namespace Harness.Checks.Complexity;

internal static class ComplexityExplanation
{
    public static string For(Language language) => language == Language.Go ? Go : CSharp;

    private static readonly string Go =
        $"""
        Rationale
          An import count describes one package. A design structure matrix (DSM) describes
          transitive package dependencies through the module as a whole. Two DSM measurements
          are compared with the ceiling the frame declares in `settings.complexity.go`:
          `averageReachableFiles` (packages, at least 1) and `largestCyclicGroupSize`
          (packages). The keys keep the names of the C# section so one family reads one shape
          of settings; the unit behind them is the package. The contract defaults are
          {Limit(ComplexitySettings.Default.AverageReachableFiles)} and {ComplexitySettings.Default.LargestCyclicGroupSize}, the
          same as the C# defaults; `harness init` writes them, and there is no separate file
          and no command that moves them. ADR-0032 defines the model, ADR-0042 replaces
          propagation cost with average reachability, ADR-0052 makes the ceiling a declared
          setting, and ADR-0055 carries the measurement to Go.

        Unit
          The node is the package, not the file. Files of one package see each other without
          an import, so a file graph would show nothing inside a package and would put every
          intra-package call outside the measurement; the package graph is the one the
          language actually has. Every tracked `.go` file outside `_test.go` contributes to
          the package of its directory; `package main` directories are packages like any other.

        Scope
          The whole module graph is measured: every package under a tracked `go.mod`, resolved
          by import path against the module path that file declares, nested modules included
          by their own `go.mod`. Test files (`_test.go`) are not read, so a test-only import
          lends no reachability. Generated, vendored and build-output locations, `vendor/`
          and `testdata/`, directories starting with `_` or `.`, files carrying the
          canonical `// Code generated ... DO NOT EDIT.` header and files under a
          `//go:build ignore` constraint (or the legacy `// +build ignore`), which the go
          tool never builds, are excluded by the Go reader; both kinds are named in the
          details.
          There is no architecture zone and no test project to draw a boundary from; the
          `scope:` line names the `main` packages, which are the composition root.

        Evidence
          Every edge is `Proven`: an import path is the compiler's own resolution, and it
          resolves to exactly one package of the tracked modules or to none. An import of a
          package outside the tracked modules — the standard library, a dependency — is an
          external import and never a node. A repository without a tracked `go.mod` is
          incomplete, not empty: import paths cannot be resolved without a module path.

        Average reachable packages formula
          Let N be the measured-package count and R(i) contain package i itself plus every
          package transitively reachable from it through imports. Average reachable packages
          = sum(|R(i)|) / N: how many packages a typical package depends on, itself included.
          The report also prints propagation cost = 100 * sum(|R(i)|) / N^2 from MacCormack,
          Rusnak and Baldwin, for comparison with the literature:
          https://doi.org/10.1287/mnsc.1060.0552
          The absolute quantity is limited because adding leaf packages does not shrink it
          through the N^2 denominator, and removing an isolated package moves it by at most a
          fraction of one package.

        Why the limit is a constant
          A Go package sees its own imports and theirs; `main` sees the whole program. A
          product built from small packages with a shallow import tree keeps the average near
          the C# default whether it has ten packages or a hundred, because adding a package
          adds a node of the same reach and the `main` packages stay few. The contract carries
          the C# value {Limit(ComplexitySettings.Default.AverageReachableFiles)} to Go unchanged; the first Go
          pilot measured below it. A repository whose packages are larger by convention may
          declare another ceiling in settings, where the change is reviewed with the frame.

        Largest cyclic group size formula
          Collapse the directed package graph into strongly connected components; the
          measurement is the package count of the largest cyclic component. The Go compiler
          refuses an import cycle, so a compiling repository always reports 0, and the
          setting keeps the shape of the family rather than adding a Go-specific one. A value
          above 0 can only come from source that does not compile.

        Computation
          The SCC condensation is a DAG. Reachability is computed as bit sets in reverse
          topological order, without recursion, reflection, compiler services or external
          dependencies, so the calculation remains compatible with NativeAOT.

        Limits
          The graph is the lexical import graph of the tracked source, not runtime calls,
          plugins, reflection or `go:generate` output that is not tracked. Build constraints
          are not evaluated beyond the `ignore` tag standing alone: every other file of a
          directory contributes its imports whatever `//go:build` says, so platform-specific
          files widen the reach of their package. A
          directory whose files declare different package names is one node named by its
          first file. `go.work` is not read; only `go.mod` names a module. `replace` and
          `require` directives are not read: an import resolves only when its path starts
          with the module path of a tracked `go.mod`.

        Policy
          Exceeding either limit is blocking when the tracked policy for this check is
          required, and no package is exempt from the measurement. A repository may run the
          whole check `advisory` or `off`, or declare a different ceiling in settings; both
          are visible in the tracked frame and reviewed like any other change to it.

        Remediation
          The report names the five packages outside the `main` packages whose own reach
          |R(i)| is largest: cutting their imports — moving a shared type below them,
          depending on a small interface package instead of an implementation, or splitting
          a package that serves two callers — lowers the average the most. Moving packages
          into `internal/`, marking files generated, raising the ceiling or widening the
          policy does not reduce the graph.

        Decisions
          adrs/0032-topology-over-thresholds.md
          adrs/0042-dsm-over-the-product-in-files.md
          adrs/0052-dsm-ceiling-is-a-declared-setting.md
          adrs/0055-go-language-axis.md
        """;

    private static readonly string CSharp =
        $"""
        Rationale
          A dependency count describes one file. A design structure matrix (DSM) describes
          transitive file dependencies through the product as a whole. Two DSM measurements
          are compared with the ceiling the frame declares in `settings.complexity.csharp`:
          `averageReachableFiles` (files, at least 1) and `largestCyclicGroupSize` (files). The contract defaults are
          {Limit(ComplexitySettings.Default.AverageReachableFiles)} files and {ComplexitySettings.Default.LargestCyclicGroupSize},
          the values of sliced-dotnet/1; `harness init` writes them, and there is no separate
          file and no command that moves them. ADR-0032 defines the model, ADR-0042 replaces
          propagation cost with average reachable files, ADR-0048 draws the product boundary without a zone,
          and ADR-0052 makes the ceiling a declared setting with contract defaults.

        Scope
          When `architecture` names sliced-dotnet/1, the DSM measures the files inside the
          architecture zones the shape check discovers: a directory holding Application/
          and the canonical layers below it. Tests, tooling and samples outside a zone are
          not nodes, and an edge into them lends no reachability. This is the same line
          the shape check draws, read from the tree, so no frame answer and no list of
          exclusions can move a file across it. Without a zone — a standalone library or a
          repository that does not follow sliced-dotnet — the product is every authored file
          whose nearest tracked project file is not a test project. A project is a test
          project when its own XML references Microsoft.NET.Test.Sdk, xunit, NUnit, MSTest or
          TUnit, sets IsTestProject, or uses MSTest.Sdk; the report names the test projects
          it left out. Only a repository without any test project is measured whole.

        Discovery
          Every authored, Git-tracked `.cs` file in scope is one node. Generated, vendored
          and build-output files are excluded by the C# source reader. Multiple type
          references between the same two files collapse to one directed file edge.
          Intra-file references do not create an edge.

        Evidence
          Only `Proven` references enter the DSM: the referenced name occurs in a type-only
          C# position and resolves to exactly one authored declaration. `Inferred` references
          are omitted so an uncertain lexical match cannot inflate either measurement.

        Average reachable files formula
          Let N be the measured-file count and R(i) contain file i itself plus every file
          transitively reachable from it. Average reachable files = sum(|R(i)|) / N, in files: how many
          files are reachable from a typical file through dependencies, itself included. The report also
          prints propagation cost = 100 * sum(|R(i)|) / N^2 from MacCormack, Rusnak and
          Baldwin, for comparison with the literature (Linux 5.16%, Mozilla 17.35%, Mozilla
          after redesign 2.78%): https://doi.org/10.1287/mnsc.1060.0552
          Average reachable files is the limited value because it is an absolute quantity: adding leaf
          files does not shrink it through the N^2 denominator, and removing an isolated
          file moves it by at most a fraction of one file.

        Why the limit is a constant
          Under sliced-dotnet/1 a file sees its own slice and the layers below it, so its
          reach is bounded by the depth of one vertical slice, and only the composition root
          in Host sees the whole product. Average reachable files is therefore approximately the average
          reach inside a slice plus the number of files that see everything. Neither term
          grows with the number of slices: adding a slice adds files of the same reach, and
          Host stays a handful of files. A product that follows the standard keeps average reachable files
          near {Limit(ComplexitySettings.Default.AverageReachableFiles)} files whether it has five slices or
          fifty, so the default describes the standard, not the repository. A separate tracked
          ratchet was tried first and was raised by agents to the current measurement under
          every feature; the ceiling now sits among the other settings of the frame, where a
          change to it is a reviewed change of the contract rather than a routine budget bump.

        Largest cyclic group size formula
          Collapse the directed file graph into strongly connected components. Largest cyclic group size is
          the file count of the largest cyclic component; its percentage is 100 * size / N.
          It measures the entire mutually reachable group, not the length of a simple cycle.
          Singleton acyclic components do not form a cyclic group, so a DAG reports zero. This follows the
          core-periphery method of Baldwin, MacCormack and Rusnak:
          https://www.sciencedirect.com/science/article/pii/S0048733314001012
          Files inside a cyclic group take about three times the lines per bug fix and cost up to
          half of a developer's productivity (Sturtevant, MIT 2013), and the fix is always the
          same — remove cyclic dependencies — so the limit is zero.

        Computation
          The SCC condensation is a DAG. Reachability is computed as bit sets in reverse
          topological order, without recursion, reflection, compiler services or external
          dependencies, so the calculation remains compatible with NativeAOT.

        Limits
          The result measures the lexical graph the harness can prove, not runtime calls,
          reflection, generated code or ambiguous names. Missing edges conservatively
          underestimate coupling. A tracked file whose first lines carry an
          `<auto-generated>` marker is not read at all; the report names such files inside
          the scope, because that marker is one line away from any hub file and a reviewer
          should see it. Cross-language file dependencies are not yet represented. Test
          projects are recognised from their own tracked XML without MSBuild evaluation, so a
          marker inherited only through Directory.Build.props is not seen and that project
          stays in the measurement; the `scope:` line makes the decision visible.

        Policy
          Exceeding either limit is blocking when the tracked policy for this check is
          required, and no file is exempt from the measurement. A repository may run the
          whole check `advisory` or `off`, or declare a different ceiling in settings; both
          are visible in the tracked frame and reviewed like any other change to it.

        Remediation
          The report names the five files outside Host whose own
          reach |R(i)| is largest: they reach the most dependencies, so cutting their
          outgoing edges — moving a shared concept below them, depending on a slice's
          Contracts/ instead of its internals, or splitting a hub that serves two slices —
          lowers average reachable files the most. A cycle is broken by removing one of its edges or
          extracting the lower-level concept both sides need. Moving files out of the zone,
          marking them generated, raising the ceiling or widening the policy does not reduce
          the graph.

        Decisions
          adrs/0032-topology-over-thresholds.md
          adrs/0042-dsm-over-the-product-in-files.md
          adrs/0048-dsm-product-boundary-without-a-zone.md
          adrs/0052-dsm-ceiling-is-a-declared-setting.md
        """;

    private static string Limit(double value) => value.ToString("F1", CultureInfo.InvariantCulture);
}

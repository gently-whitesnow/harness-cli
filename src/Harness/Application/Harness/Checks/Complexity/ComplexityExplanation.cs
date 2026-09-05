using System.Globalization;
using Harness.Config;

namespace Harness.Checks.Complexity;

internal static class ComplexityExplanation
{
    public static readonly string Text =
        $"""
        Rationale
          A dependency count describes one file. A design structure matrix (DSM) describes
          how a change can propagate through the product as a whole. Two DSM measurements
          are compared with the ceiling the frame declares in `settings.complexity.csharp`:
          `meanReach` (files, at least 1) and `coreSize` (files). The contract defaults are
          {Limit(ComplexitySettings.Default.MeanReach)} files and {ComplexitySettings.Default.CoreSize},
          the values of sliced-dotnet/1; `harness init` writes them, and there is no separate
          file and no command that moves them. ADR-0032 defines the model, ADR-0042 replaces
          propagation cost with mean reach, ADR-0048 draws the product boundary without a zone,
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

        Mean reach formula
          Let N be the measured-file count and R(i) contain file i itself plus every file
          transitively reachable from it. Mean reach = sum(|R(i)|) / N, in files: how many
          files a change to a typical file can reach, itself included. The report also
          prints propagation cost = 100 * sum(|R(i)|) / N^2 from MacCormack, Rusnak and
          Baldwin, for comparison with the literature (Linux 5.16%, Mozilla 17.35%, Mozilla
          after redesign 2.78%): https://doi.org/10.1287/mnsc.1060.0552
          Mean reach is the limited value because it is an absolute quantity: adding leaf
          files does not shrink it through the N^2 denominator, and removing an isolated
          file moves it by at most a fraction of one file.

        Why the limit is a constant
          Under sliced-dotnet/1 a file sees its own slice and the layers below it, so its
          reach is bounded by the depth of one vertical slice, and only the composition root
          in Host sees the whole product. Mean reach is therefore approximately the average
          reach inside a slice plus the number of files that see everything. Neither term
          grows with the number of slices: adding a slice adds files of the same reach, and
          Host stays a handful of files. A product that follows the standard keeps mean reach
          near {Limit(ComplexitySettings.Default.MeanReach)} files whether it has five slices or
          fifty, so the default describes the standard, not the repository. A separate tracked
          ratchet was tried first and was raised by agents to the current measurement under
          every feature; the ceiling now sits among the other settings of the frame, where a
          change to it is a reviewed change of the contract rather than a routine budget bump.

        Core size formula
          Collapse the directed file graph into strongly connected components. Core size is
          the file count of the largest cyclic component and 100 * core files / N. Singleton
          acyclic components do not form a core, so a DAG reports zero. This follows the
          core-periphery method of Baldwin, MacCormack and Rusnak:
          https://www.sciencedirect.com/science/article/pii/S0048733314001012
          Files inside a core take about three times the lines per bug fix and cost up to
          half of a developer's productivity (Sturtevant, MIT 2013), and the fix is always the
          same — break one edge of the cycle — so the limit is zero.

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
          reach |R(i)| is largest: a change there travels furthest, so cutting their
          outgoing edges — moving a shared concept below them, depending on a slice's
          Contracts/ instead of its internals, or splitting a hub that serves two slices —
          lowers mean reach the most. A cycle is broken by removing one of its edges or
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

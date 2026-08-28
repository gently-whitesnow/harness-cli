namespace Harness.Checks.Complexity;

internal static class ComplexityExplanation
{
    public const string Text =
        """
        Rationale
          A dependency count describes one file. A design structure matrix (DSM) describes
          how a change can propagate through the repository as a whole. These measurements
          are compared with one repository-wide, tracked ratchet budget. A regression is
          blocking; an improvement is visible until the repository records it.

        Discovery
          Every authored, Git-tracked `.cs` file is one node. Generated, vendored and
          build-output files are excluded by the C# source reader. Multiple type references
          between the same two files collapse to one directed file edge. Intra-file references
          do not create an edge.

        Evidence
          Only `Proven` references enter the DSM: the referenced name occurs in a type-only
          C# position and resolves to exactly one authored declaration. `Inferred` references
          are omitted so an uncertain lexical match cannot inflate either measurement.

        Propagation cost formula
          Let N be the authored-file count and R(i) contain file i itself plus every file
          transitively reachable from it. Propagation cost = 100 * sum(|R(i)|) / N^2.
          The diagonal is included, as in a DSM visibility matrix. Reference values reported
          by MacCormack, Rusnak and Baldwin are Linux 5.16%, Mozilla 17.35%, and Mozilla after
          redesign 2.78%:
          https://doi.org/10.1287/mnsc.1060.0552

        Core size formula
          Collapse the directed file graph into strongly connected components. Core size is
          the file count of the largest cyclic component and 100 * core files / N. Singleton
          acyclic components do not form a core, so a DAG reports zero. This follows the
          core-periphery method of Baldwin, MacCormack and Rusnak:
          https://www.sciencedirect.com/science/article/pii/S0048733314001012

        Computation
          The SCC condensation is a DAG. Reachability is computed as bit sets in reverse
          topological order, without recursion, reflection, compiler services or external
          dependencies, so the calculation remains compatible with NativeAOT.

        Limits
          The result measures the lexical graph the harness can prove, not runtime calls,
          reflection, generated code or ambiguous names. Missing edges conservatively
          underestimate coupling. Authored test files are deliberately part of the graph and
          affect both N and its edges. Cross-language file dependencies are not yet represented.

        Remediation
          Run `harness budget update` once to create `.harness.budget.json`. Commit both files.
          A rising propagation cost means more files lie downstream of typical changes. A
          growing core means more files must change as a mutually dependent group; break an
          edge or extract a lower-level concept. The update command only lowers budgets;
          raising one requires an explicit tracked edit and review.
        """;
}

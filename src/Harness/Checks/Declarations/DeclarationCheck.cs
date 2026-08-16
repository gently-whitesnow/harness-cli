using Harness.Config;

namespace Harness.Checks.Declarations;

/// <summary>One tracked thing the harness recognized, and what it recognized about it.</summary>
internal sealed record EvidenceItem(string Location, string Description);

/// <summary>
/// One question of the harness frame, asked the same way of every repository: does this
/// repository own the thing, and where is the proof?
/// </summary>
/// <remarks>
/// The harness does not run the thing and does not infer that it exists. The repository
/// answers in its own tracked <c>.harness.json</c>, and the answer is held against Git. That
/// division is the whole design. A declaration is a claim the repository makes to everyone
/// who reads it — reviewer, agent, CI — and Git is what stops the claim from being free: an
/// address that is not tracked, or an absence Git plainly refutes, is a violation. What Git
/// cannot refute stays a claim, reported as one, never quietly upgraded to a pass.
/// </remarks>
internal abstract class DeclarationCheck : IRepositoryCheck
{
    /// <summary>Enough locations to act on; the rest are counted, not listed.</summary>
    private const int ShownLocations = 3;

    /// <summary>The question's key in `declarations`, and the suffix of this check's id.</summary>
    protected abstract string Key { get; }

    /// <summary>The thing being asked about, as a plural noun phrase the report can use.</summary>
    protected abstract string Subject { get; }

    /// <summary>An address a repository of this kind would plausibly give.</summary>
    protected abstract string AddressExample { get; }

    /// <summary>What the harness would recognize in Git as a sign of the thing.</summary>
    protected abstract IReadOnlyList<EvidenceItem> Evidence(CheckContext context);

    /// <summary>What it looked for, in the words `explain` prints.</summary>
    protected abstract string LookedFor { get; }

    /// <summary>
    /// Whether the recognized evidence is specific enough to call a denial wrong. True where
    /// what the harness recognizes exists for one purpose only. False where the same
    /// evidence would appear in a repository that honestly answered "no" — there the
    /// evidence is still shown as a hint, but it never fails the run.
    /// </summary>
    protected virtual bool EvidenceRefutes => true;

    public string Id => $"{HarnessConfig.DeclarationGroup}.{Key}";

    public string Group => HarnessConfig.DeclarationGroup;

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

    public CheckEvaluation Evaluate(CheckContext context)
    {
        if (context.Config is null)
        {
            return CheckEvaluation.Incomplete(
                $"the harness frame could not be read, so what this repository claims about {Subject} is not "
                    + $"established: {context.ConfigFailure}");
        }

        var declaration = context.Config.Declared(Key);
        var evidence = Evidence(context);

        return declaration?.Kind switch
        {
            null => Undeclared(evidence),
            DeclarationKind.Proven => Proven(context, declaration),
            DeclarationKind.Asserted => Asserted(declaration, evidence),
            DeclarationKind.Absent => Refutable(declaration, evidence, "no"),
            _ => Refutable(declaration, evidence, "not applicable"),
        };
    }

    /// <summary>
    /// The address is the answer, so the only thing left to verify is that it is real. That
    /// the address exists is not evidence of what lives at it: the harness reads no test
    /// bodies and judges no coverage, and the report says so rather than implying otherwise.
    /// </summary>
    private CheckEvaluation Proven(CheckContext context, Declaration declaration)
    {
        var tracked = context.Repository.TrackedEntries.Select(entry => entry.Path).ToList();
        var missing = declaration.Paths
            .Where(path => !tracked.Any(candidate =>
                string.Equals(candidate, path, StringComparison.Ordinal)
                || candidate.StartsWith(path + "/", StringComparison.Ordinal)))
            .ToList();

        if (missing.Count > 0)
        {
            return CheckEvaluation.From(missing
                .Select(path => new Finding(
                    FindingSeverity.Blocking,
                    path,
                    $"`.harness.json` declares {Subject} at this address and Git tracks nothing there. Point the "
                        + "declaration at what exists, or answer with `present` and a reason."))
                .ToList());
        }

        return CheckEvaluation.Passed(
            $"declared and proven — {Subject} at {Locations(declaration.Paths)}, "
                + $"{(declaration.Paths.Count == 1 ? "which Git tracks" : "all of which Git tracks")}. That the "
                + "address is real is not evidence of what lives at it.");
    }

    /// <summary>
    /// A claim with no address. It is not a violation — plenty of real machinery has no one
    /// file to point at — but it is not a proven answer either, and the report keeps the two
    /// apart. When Git happens to show something, the report names it: that is usually the
    /// address the repository could have given.
    /// </summary>
    private CheckEvaluation Asserted(Declaration declaration, IReadOnlyList<EvidenceItem> evidence)
        => CheckEvaluation.ReadinessGap(
            $"declared without an address — \"{declaration.Reason}\". The harness took the claim and verified "
                + "nothing."
                + (evidence.Count > 0
                    ? $" Git shows {Describe(evidence)}, which the declaration could name as its address."
                    : $" Nothing recognized backs it up (looked for {LookedFor})."));

    /// <summary>
    /// An answer Git is able to refute: the repository says the thing is not there, or not
    /// its question at all, while tracked evidence says otherwise. This is the one place the
    /// frame can fail on its own terms, and the finding points at the evidence rather than
    /// at the config, because that is the file a reader has to look at to decide who is
    /// right.
    /// </summary>
    private CheckEvaluation Refutable(Declaration declaration, IReadOnlyList<EvidenceItem> evidence, string answer)
    {
        if (evidence.Count > 0 && EvidenceRefutes)
        {
            return CheckEvaluation.From(evidence
                .Select(item => new Finding(
                    FindingSeverity.Blocking,
                    item.Location,
                    $"`.harness.json` answers \"{answer}\" for {Subject} — \"{declaration.Reason}\" — and this "
                        + $"tracked file {item.Description}. Correct the declaration, or remove what contradicts it."))
                .ToList());
        }

        return declaration.Kind == DeclarationKind.NotApplicable
            ? CheckEvaluation.NotApplicable(
                $"declared not applicable — \"{declaration.Reason}\". Nothing recognized contradicts that "
                    + $"(looked for {LookedFor}).")
            : CheckEvaluation.ReadinessGap(
                $"declared absent — \"{declaration.Reason}\". The repository owns no {Subject}, and says so on "
                    + "purpose. Set this check to `required` in `policy` once that should stop being acceptable.");
    }

    /// <summary>
    /// The question was not answered at all. This is the state the frame exists to make
    /// visible, so the message is written as an instruction: it names the key, the forms of
    /// answer, and an address this repository could plausibly give.
    /// </summary>
    private CheckEvaluation Undeclared(IReadOnlyList<EvidenceItem> evidence)
        => CheckEvaluation.ReadinessGap(
            $"undeclared — `.harness.json` does not say whether this repository owns {Subject}. Answer under "
                + $"`declarations.{Key}` with `{{ \"paths\": [\"{AddressExample}\"] }}`, or with `present` and a "
                + "`reason`, or with `applicable: false` and a `reason`."
                + (evidence.Count > 0 ? $" Git shows {Describe(evidence)}." : ""));

    private static string Describe(IReadOnlyList<EvidenceItem> evidence)
    {
        var shown = string.Join("; ", evidence
            .Take(ShownLocations)
            .Select(item => $"{item.Location} {item.Description}"));

        return evidence.Count <= ShownLocations
            ? shown
            : $"{shown} and {evidence.Count - ShownLocations} more ({evidence.Count} total)";
    }

    private static string Locations(IReadOnlyList<string> paths)
    {
        var shown = string.Join(", ", paths.Take(ShownLocations));
        return paths.Count <= ShownLocations
            ? shown
            : $"{shown} and {paths.Count - ShownLocations} more ({paths.Count} total)";
    }
}

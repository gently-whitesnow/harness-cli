using Harness.Checks.DotNet;
using Harness.Checks.Web;

namespace Harness.Checks.Capabilities;

/// <summary>
/// What the harness may say about one capability, weakest claim first. The order is the
/// precedence used when the stacks of one repository answer differently: evidence found
/// anywhere outranks uncertainty, and uncertainty outranks an absence that was never proved.
/// </summary>
internal enum CapabilityVerdict
{
    /// <summary>The stack is here and none of the evidence the harness recognizes is.</summary>
    NotDetected,

    /// <summary>There is evidence, and it does not settle whether the capability exists.</summary>
    Unknown,

    /// <summary>Recognized evidence of the capability is tracked in the repository.</summary>
    Detected,

    /// <summary>A gate ran that evidence in this run, and it passed.</summary>
    Executed,
}

/// <summary>One tracked location that carries recognized evidence, and what was recognized.</summary>
internal sealed record CapabilityCarrier(string Location, string Recognized);

/// <summary>What one stack says about one capability, and how it says it.</summary>
/// <param name="Detail">The observation in the report's own words, without the verdict.</param>
internal sealed record CapabilityObservation(CapabilityVerdict Verdict, string Detail);

/// <summary>
/// Shared shape of the capability readers: look at the surfaces the other gates already
/// discovered, decide what the tracked evidence supports, and say exactly that.
/// </summary>
/// <remarks>
/// The five words this check may use are not interchangeable. `detected` means recognized
/// evidence is tracked. `executed` means a gate ran that same evidence in this run and it
/// passed — never merely that some gate of the same stack passed, because a command that
/// ran a different thing proves nothing about this one. `not detected` means the harness
/// looked and found nothing it recognizes, never that the repository lacks the capability:
/// the list of recognized evidence is written here and is always potentially behind the
/// repository. `unknown` means the evidence exists and does not settle the question. `not
/// applicable` means there is no stack for the capability to live in. None of them fails a
/// run: capability evidence is advisory in v0, so a missing or uncertain capability is a
/// visible readiness gap rather than a verdict on code the harness never read.
/// </remarks>
internal abstract class CapabilityCheck : IRepositoryCheck
{
    /// <summary>Enough evidence locations to act on; the rest are counted, not listed.</summary>
    private const int ShownLocations = 3;

    public abstract string Id { get; }

    public string Group => "capability";

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

    /// <summary>The capability in the words the report uses, as a plural noun phrase.</summary>
    protected abstract string Capability { get; }

    /// <summary>Package names in a tracked .NET project that are evidence of this capability.</summary>
    protected abstract IReadOnlyList<string> DotNetPackages { get; }

    /// <summary>Script names in the web manifest that are evidence of this capability.</summary>
    protected abstract IReadOnlyList<string> WebScripts { get; }

    /// <summary>Declared package names that are evidence of this capability.</summary>
    protected abstract IReadOnlyList<string> WebPackages { get; }

    /// <summary>
    /// Whether a passing `web.test` ran the evidence this capability detects. False by
    /// default: `web.test` runs one test script, and a repository's end-to-end runner or
    /// boundary linter is not that script, so its passing says nothing about them.
    /// </summary>
    protected virtual bool WebGateRunsTheEvidence => false;

    /// <summary>What the harness looked for in .NET projects, for the report that found none.</summary>
    protected virtual IReadOnlyList<string> DotNetLookedFor => DotNetPackages;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var dotnet = DotNetSurface.Discover(context.Repository);
        var web = WebSurface.Discover(context.Repository);

        if (dotnet.Kind == DotNetSurfaceKind.Absent && web.Kind == WebSurfaceKind.Absent)
        {
            return CheckEvaluation.NotApplicable(
                $"the repository has no stack this version can read {Capability} from ({dotnet.Reason}; {web.Reason})");
        }

        var observations = new List<CapabilityObservation>();
        Add(observations, InDotNet(context, dotnet));
        Add(observations, InWeb(context, web));

        // One stack's passing command does not vouch for another's evidence, so `executed`
        // is only reached when every stack that found evidence also ran it.
        var verdict = observations.Min(observation => observation.Verdict) == CapabilityVerdict.Executed
            ? CapabilityVerdict.Executed
            : observations.Max(observation => observation.Verdict);

        // The observations that carried the verdict explain it, and an uncertain stack is
        // always reported even when another stack found something: evidence over here does
        // not answer the question over there.
        var detail = string.Join("; ", observations
            .Where(observation => observation.Verdict >= verdict || observation.Verdict == CapabilityVerdict.Unknown)
            .Select(observation => observation.Detail));

        var reason = $"{Word(verdict)} — {detail}.{Caveat(verdict)}";
        return verdict >= CapabilityVerdict.Detected
            ? CheckEvaluation.Passed(reason)
            : CheckEvaluation.ReadinessGap(reason);
    }

    private static void Add(List<CapabilityObservation> observations, CapabilityObservation? observation)
    {
        if (observation is not null)
        {
            observations.Add(observation);
        }
    }

    /// <summary>
    /// What one tracked .NET project carries for this capability, named as the report will
    /// name it. Empty when the project carries nothing recognized.
    /// </summary>
    /// <remarks>
    /// Overridden where discovery has already classified the projects, so a capability whose
    /// evidence the surface already holds is not classified a second time.
    /// </remarks>
    protected virtual IReadOnlyList<CapabilityCarrier> CarriedBy(string projectPath, string projectText)
        => DotNetPackages
            .Where(package => projectText.Contains($"\"{package}", StringComparison.OrdinalIgnoreCase))
            .Take(1)
            .Select(package => new CapabilityCarrier(projectPath, package))
            .ToList();

    private static string Word(CapabilityVerdict verdict)
        => verdict switch
        {
            CapabilityVerdict.Executed => "executed",
            CapabilityVerdict.Detected => "detected",
            CapabilityVerdict.Unknown => "unknown",
            _ => "not detected",
        };

    /// <summary>
    /// What the verdict does not mean. Every one of these sentences exists because the
    /// opposite reading is the one a reader in a hurry would take.
    /// </summary>
    private string Caveat(CapabilityVerdict verdict)
        => verdict switch
        {
            CapabilityVerdict.Executed => $" That the {Capability} pass is not evidence that they are complete.",
            CapabilityVerdict.Detected => $" Evidence that {Capability} exist is not evidence of what they cover.",
            CapabilityVerdict.Unknown =>
                $" The harness reports neither presence nor absence of {Capability} from evidence like this.",
            _ => $" Absence of recognized evidence is not proof the repository has no {Capability}: it may have "
                + "them in a form this version does not recognize.",
        };

    private CapabilityObservation? InDotNet(CheckContext context, DotNetSurface surface)
    {
        switch (surface.Kind)
        {
            case DotNetSurfaceKind.Absent:
                return null;

            case DotNetSurfaceKind.Ambiguous:
                return new CapabilityObservation(CapabilityVerdict.Unknown, surface.Reason!);
        }

        var carriers = new List<CapabilityCarrier>();
        var unreadable = new List<string>();

        foreach (var project in surface.Projects)
        {
            var (text, _) = context.Repository.ReadTrackedText(project);

            // A project the harness cannot read is the one place its answer is genuinely
            // unknown: unlike a gate, this check has no command whose failure would surface
            // the same unreadable file. It does not erase the evidence already found.
            if (text is null)
            {
                unreadable.Add(project.Path);
                continue;
            }

            carriers.AddRange(CarriedBy(project.Path, text));
        }

        if (carriers.Count == 0)
        {
            return unreadable.Count > 0
                ? new CapabilityObservation(
                    CapabilityVerdict.Unknown,
                    $"{Locations(unreadable)} could not be read, so what the {surface.Projects.Count} tracked "
                        + $".NET projects show about {Capability} is not established")
                : new CapabilityObservation(
                    CapabilityVerdict.NotDetected,
                    $"none of the {surface.Projects.Count} tracked .NET projects shows {Capability} "
                        + $"(looked for {string.Join(", ", DotNetLookedFor)})");
        }

        // `dotnet test` runs test projects. Evidence outside one was not run by it, whatever
        // that command's exit status was.
        var locations = carriers.Select(carrier => carrier.Location).Distinct(StringComparer.Ordinal).ToList();
        var ran = context.Passed("dotnet.test")
            && locations.All(location => surface.TestProjects.Contains(location, StringComparer.Ordinal));

        var others = surface.Projects.Count - locations.Count;
        var recognized = carriers.Select(carrier => carrier.Recognized).Distinct(StringComparer.Ordinal);

        return new CapabilityObservation(
            ran ? CapabilityVerdict.Executed : CapabilityVerdict.Detected,
            $"{Locations(locations)} carr{(locations.Count == 1 ? "ies" : "y")} {string.Join(", ", recognized)}"
                + (others > 0
                    ? $", which says nothing about the {others} other tracked .NET project{(others == 1 ? "" : "s")}"
                    : "")
                + (ran ? ", and `dotnet.test` ran them in this run and passed" : "")
                + (unreadable.Count > 0 ? $"; {Locations(unreadable)} could not be read" : ""));
    }

    private CapabilityObservation? InWeb(CheckContext context, WebSurface surface)
    {
        switch (surface.Kind)
        {
            case WebSurfaceKind.Absent:
                return null;

            case WebSurfaceKind.Ambiguous:
                return new CapabilityObservation(CapabilityVerdict.Unknown, surface.Reason!);
        }

        var found = WebScripts
            .Where(surface.Scripts.ContainsKey)
            .Select(script => $"the script `{script}`")
            .Concat(WebPackages
                .Where(surface.Dependencies.Contains)
                .Select(package => $"a dependency on {package}"))
            .ToList();

        if (found.Count == 0)
        {
            return new CapabilityObservation(
                CapabilityVerdict.NotDetected,
                $"{surface.ManifestPath} declares none of {string.Join(", ", WebScripts.Concat(WebPackages))}");
        }

        var ran = WebGateRunsTheEvidence && context.Passed("web.test");
        return new CapabilityObservation(
            ran ? CapabilityVerdict.Executed : CapabilityVerdict.Detected,
            $"{surface.ManifestPath} declares {string.Join(" and ", found)}"
                + (ran ? ", and `web.test` ran it in this run and passed" : ""));
    }

    /// <summary>
    /// Bounded evidence: enough locations to open, then a count. The report must stay
    /// readable on a repository with hundreds of projects.
    /// </summary>
    private static string Locations(IReadOnlyList<string> paths)
    {
        var shown = string.Join(", ", paths.Take(ShownLocations));
        return paths.Count <= ShownLocations
            ? shown
            : $"{shown} and {paths.Count - ShownLocations} more ({paths.Count} total)";
    }
}

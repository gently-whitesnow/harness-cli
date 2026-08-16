using Harness.Git;
using Harness.Processes;

namespace Harness.Checks.DotNet;

/// <summary>
/// Shared shape of the .NET gates: discover the execution plan, decline honestly when the
/// stack is absent or the plan is ambiguous, then run one standard SDK command per target
/// and translate its exit status. Subclasses supply only identity, targets and arguments.
/// </summary>
internal abstract class DotNetCheck : IRepositoryCheck
{
    public abstract string Id { get; }

    public string Group => "dotnet";

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

    public CheckEvaluation Evaluate(GitRepository repository)
    {
        var surface = DotNetSurface.Discover(repository);
        switch (surface.Kind)
        {
            case DotNetSurfaceKind.Absent:
                return CheckEvaluation.NotApplicable(surface.Reason!);

            case DotNetSurfaceKind.Ambiguous:
                return CheckEvaluation.Incomplete(surface.Reason!);
        }

        var targets = Targets(surface);
        if (targets.Count == 0)
        {
            return CheckEvaluation.NotApplicable(NotApplicableReason);
        }

        // Applicability is decided from repository evidence alone, so a repository without
        // this stack never depends on the environment having an SDK.
        var sdkFailure = DotNetSdk.Verify(repository.RootPath);
        if (sdkFailure is not null)
        {
            return CheckEvaluation.Incomplete(sdkFailure);
        }

        var findings = new List<Finding>();
        var commands = new List<ExecutedCommand>();

        foreach (var target in targets)
        {
            var run = ProcessRunner.Run(
                "dotnet", Arguments(target, surface), repository.RootPath, DotNetSdk.InvariantOutput);
            commands.Add(ExecutedCommand.From(run));

            if (run.Failure is not null)
            {
                return CheckEvaluation.Incomplete(run.Failure, commands);
            }

            if (run.ExitCode == 0)
            {
                continue;
            }

            // A gate that could not obtain its inputs has not verified anything, so it
            // ends the run as incomplete instead of blaming the code it never examined.
            var restoreFailure = DotNetDiagnostics.RestoreFailure(run);
            if (restoreFailure is not null)
            {
                return CheckEvaluation.Incomplete(restoreFailure, commands);
            }

            findings.AddRange(Interpret(repository.RootPath, target, run));
        }

        return CheckEvaluation.From(findings, commands);
    }

    /// <summary>Why this gate does not apply to a repository that does have a .NET surface.</summary>
    protected abstract string NotApplicableReason { get; }

    protected abstract IReadOnlyList<string> Targets(DotNetSurface surface);

    protected abstract IReadOnlyList<string> Command(string target);

    /// <summary>
    /// Whether the command accepts MSBuild properties. `dotnet format` is not an MSBuild
    /// command and rejects them outright, which would turn a restore constraint into a
    /// fabricated formatting violation.
    /// </summary>
    protected virtual bool TakesMsBuildProperties => true;

    protected virtual IReadOnlyList<Finding> Interpret(string rootPath, string target, ProcessResult run)
        => DotNetDiagnostics.Locate(rootPath, target, run);

    /// <summary>
    /// The gate's own command, plus the restore constraint the repository has asked for.
    /// A tracked lock file is tracked configuration: restoring must honour it rather than
    /// rewrite it, because the harness observes and never changes what it is checking.
    /// </summary>
    private IReadOnlyList<string> Arguments(string target, DotNetSurface surface)
    {
        var command = Command(target);
        return surface.HasLockFiles && TakesMsBuildProperties
            ? [.. command, "-p:RestoreLockedMode=true"]
            : command;
    }
}

/// <summary>Formatting as verification only; the harness never applies a fix.</summary>
internal sealed class DotNetFormatCheck : DotNetCheck
{
    public override string Id => "dotnet.format";

    public override string Summary => ".NET formatting verification";

    public override string Explanation => DotNetExplanations.Format;

    protected override string NotApplicableReason => "no .NET target to verify formatting for";

    protected override IReadOnlyList<string> Targets(DotNetSurface surface) => surface.BuildTargets;

    protected override IReadOnlyList<string> Command(string target)
        => ["format", target, "--verify-no-changes"];

    protected override bool TakesMsBuildProperties => false;
}

internal sealed class DotNetBuildCheck : DotNetCheck
{
    public override string Id => "dotnet.build";

    public override string Summary => ".NET compilation";

    public override string Explanation => DotNetExplanations.Build;

    protected override string NotApplicableReason => "no .NET target to compile";

    protected override IReadOnlyList<string> Targets(DotNetSurface surface) => surface.BuildTargets;

    protected override IReadOnlyList<string> Command(string target)
        => ["build", target, "--nologo", "--verbosity", "quiet"];
}

internal sealed class DotNetTestCheck : DotNetCheck
{
    public override string Id => "dotnet.test";

    public override string Summary => ".NET tests";

    public override string Explanation => DotNetExplanations.Test;

    protected override string NotApplicableReason
        => "no tracked project declares a .NET test framework, so there are no tests to run";

    protected override IReadOnlyList<string> Targets(DotNetSurface surface) => surface.TestTargets;

    /// <summary>
    /// Gates are independent, so the test gate builds what it runs instead of depending on
    /// the build gate having been selected.
    /// </summary>
    /// <remarks>
    /// The runner's default verbosity is what names the failing tests; quieter output
    /// reports only that something failed, which is not evidence a reader can act on.
    /// </remarks>
    protected override IReadOnlyList<string> Command(string target)
        => ["test", target, "--nologo"];

    protected override IReadOnlyList<Finding> Interpret(string rootPath, string target, ProcessResult run)
        => DotNetDiagnostics.LocateFailedTests(rootPath, target, run);
}

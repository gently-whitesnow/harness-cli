using System.Text;
using Harness.Git;
using Harness.Processes;

namespace Harness.Checks.Web;

/// <summary>
/// The script names the harness recognizes for a role, most explicit first. One list per
/// role, shared by the gate that runs the script and by the capability reader that treats
/// the script's existence as evidence, so the two can never come to disagree about what a
/// test script is called.
/// </summary>
internal static class WebScriptNames
{
    /// <summary>
    /// The single-run variants come first: a watch-mode runner would never return, and a
    /// gate that cannot finish is not a gate.
    /// </summary>
    public static readonly IReadOnlyList<string> Test = ["test:ci", "test:run", "test:unit", "test"];
}

/// <summary>
/// Shared shape of the web gates: discover the execution plan, decline honestly when the
/// stack is absent or the evidence is ambiguous, then run one of the repository's own
/// scripts through its own package manager and translate the exit status. Subclasses
/// supply only identity and which script names they recognize.
/// </summary>
/// <remarks>
/// The harness runs commands the repository already declares. It never synthesizes one, so
/// a repository that has no such script has a visible readiness gap rather than a check
/// invented on its behalf.
/// </remarks>
internal abstract class WebCheck : IRepositoryCheck
{
    /// <summary>
    /// Flags that make a script apply a fix rather than report one. A gate is a verification,
    /// and the harness never edits tracked content, so a script carrying one of these is
    /// reported instead of run.
    /// </summary>
    private static readonly string[] MutatingFlags = ["--write", "--fix", "--apply", "--apply-unsafe"];

    /// <summary>
    /// Flags with which a tool states that it only reports. A script whose name is
    /// conventionally the fixer is run only when its command says this explicitly, because
    /// on that name silence is not evidence of restraint.
    /// </summary>
    private static readonly string[] VerifyingFlags = ["--check", "--verify", "--list-different", "--dry-run"];

    public abstract string Id { get; }

    public string Group => "web";

    public abstract string Summary { get; }

    public abstract string Explanation { get; }

    /// <summary>
    /// Script names this gate accepts, most explicit first. The names are the conventional
    /// ones across the ecosystem rather than a single repository's habits.
    /// </summary>
    protected abstract IReadOnlyList<string> ScriptNames { get; }

    /// <summary>
    /// Accepted names that the ecosystem conventionally gives to the fixing command rather
    /// than the reporting one. They are run only when the command states a verification mode.
    /// </summary>
    protected virtual IReadOnlyList<string> FixerScriptNames => [];

    /// <summary>What the gate is looking for, for the readiness gap it reports.</summary>
    protected abstract string Capability { get; }

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var repository = context.Repository;

        var surface = WebSurface.Discover(repository);
        switch (surface.Kind)
        {
            case WebSurfaceKind.Absent:
                return CheckEvaluation.NotApplicable(surface.Reason!);

            case WebSurfaceKind.Ambiguous:
                return CheckEvaluation.Incomplete(surface.Reason!);
        }

        var script = ScriptNames.FirstOrDefault(surface.Scripts.ContainsKey);
        if (script is null)
        {
            return CheckEvaluation.ReadinessGap(
                $"{surface.ManifestPath} declares no {Capability} script (looked for "
                    + $"{string.Join(", ", ScriptNames)}). The harness runs the repository's own commands and does "
                    + "not invent one.");
        }

        var command = surface.Scripts[script];
        var effective = Effective(surface, script);
        if (Token(effective, MutatingFlags) is { } mutation)
        {
            return CheckEvaluation.ReadinessGap(
                $"`{script}` applies a fix rather than reporting one (`{command}` passes {mutation}), so it is not "
                    + $"a {Capability} the harness may run. Add a script that verifies without writing.");
        }

        if (FixerScriptNames.Contains(script, StringComparer.Ordinal) && Token(effective, VerifyingFlags) is null)
        {
            return CheckEvaluation.ReadinessGap(
                $"`{script}` is conventionally the command that rewrites files, and `{command}` does not state a "
                    + $"verification mode ({string.Join(", ", VerifyingFlags)}). The harness will not run it to find "
                    + $"out. Add a {Capability} script that only reports.");
        }

        // Scripts run the repository's own dependencies. Installing them is the caller's
        // decision, so an uninstalled project makes verification incomplete rather than
        // producing a failure that describes the environment instead of the code.
        if (surface.DeclaresDependencies && !WebPackageManager.DependenciesInstalled(surface))
        {
            return CheckEvaluation.Incomplete(
                $"{surface.ManifestPath} declares dependencies but node_modules is absent, so "
                    + $"`{surface.PackageManager} run {script}` would not verify the repository. Install the "
                    + "dependencies yourself; the harness does not install them.");
        }

        var unusable = WebPackageManager.Verify(surface);
        if (unusable is not null)
        {
            return CheckEvaluation.Incomplete(unusable);
        }

        var run = WebPackageManager.RunScript(surface, script);
        var commands = new List<ExecutedCommand> { ExecutedCommand.From(run) };

        if (run.Failure is not null)
        {
            return CheckEvaluation.Incomplete(run.Failure, commands);
        }

        return run.ExitCode == 0
            ? CheckEvaluation.From([], commands)
            : CheckEvaluation.From(
                WebDiagnostics.Locate(repository.RootPath, surface.ManifestPath, run), commands);
    }

    /// <summary>
    /// Everything the script will actually run: its own command, plus the commands of the
    /// scripts it delegates to. A script that only calls another one hides what it does, so
    /// judging it by its own text alone would let `"format": "npm run format:fix"` through
    /// the guard that exists to keep the harness from editing tracked content.
    /// </summary>
    private static string Effective(WebSurface surface, string script)
    {
        var seen = new HashSet<string>(StringComparer.Ordinal);
        var pending = new Queue<string>([script]);
        var text = new StringBuilder();

        while (pending.Count > 0)
        {
            var current = pending.Dequeue();
            if (!seen.Add(current) || !surface.Scripts.TryGetValue(current, out var command))
            {
                continue;
            }

            text.Append(command).Append(' ');
            foreach (var delegated in Delegated(surface, command))
            {
                pending.Enqueue(delegated);
            }
        }

        return text.ToString();
    }

    /// <summary>
    /// The scripts a command hands the work to. Only a `run &lt;name&gt;` invocation counts: a
    /// script name that merely appears as an argument, as in `eslint --format json`, is not
    /// a delegation and must not drag another script's flags into this gate's judgement.
    /// </summary>
    private static IEnumerable<string> Delegated(WebSurface surface, string command)
    {
        var tokens = Tokens(command).ToList();
        for (var index = 1; index < tokens.Count; index++)
        {
            if (tokens[index - 1] == "run" && surface.Scripts.ContainsKey(tokens[index]))
            {
                yield return tokens[index];
            }
        }
    }

    /// <summary>The first of the given flags a command passes, or null when it passes none.</summary>
    private static string? Token(string command, IReadOnlyList<string> flags)
        => Tokens(command).FirstOrDefault(token => flags.Contains(token, StringComparer.Ordinal));

    private static IEnumerable<string> Tokens(string command)
        => command.Split([' ', '\t', '\n', '\r', '"', '\''], StringSplitOptions.RemoveEmptyEntries);
}

/// <summary>Formatting as verification only; the harness never applies a fix.</summary>
internal sealed class WebFormatCheck : WebCheck
{
    public override string Id => "web.format";

    public override string Summary => "web formatting verification";

    public override string Explanation => WebExplanations.Format;

    protected override string Capability => "formatting verification";

    protected override IReadOnlyList<string> ScriptNames =>
        ["format:check", "format:verify", "check-format", "format"];

    protected override IReadOnlyList<string> FixerScriptNames => ["format"];
}

internal sealed class WebLintCheck : WebCheck
{
    public override string Id => "web.lint";

    public override string Summary => "web lint";

    public override string Explanation => WebExplanations.Lint;

    protected override string Capability => "lint";

    protected override IReadOnlyList<string> ScriptNames => ["lint:check", "lint"];
}

internal sealed class WebTypecheckCheck : WebCheck
{
    public override string Id => "web.typecheck";

    public override string Summary => "web type checking";

    public override string Explanation => WebExplanations.Typecheck;

    protected override string Capability => "type checking";

    protected override IReadOnlyList<string> ScriptNames => ["typecheck", "type-check", "check-types"];
}

internal sealed class WebTestCheck : WebCheck
{
    public override string Id => "web.test";

    public override string Summary => "web tests";

    public override string Explanation => WebExplanations.Test;

    protected override string Capability => "test";

    protected override IReadOnlyList<string> ScriptNames => WebScriptNames.Test;
}

internal sealed class WebBuildCheck : WebCheck
{
    public override string Id => "web.build";

    public override string Summary => "web build";

    public override string Explanation => WebExplanations.Build;

    protected override string Capability => "build";

    protected override IReadOnlyList<string> ScriptNames => ["build"];
}

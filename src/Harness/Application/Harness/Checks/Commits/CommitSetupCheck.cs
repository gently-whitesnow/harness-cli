using Harness.Commits;

namespace Harness.Checks.Commits;

internal sealed class CommitSetupCheck(ICommitIntegration integration) : IRepositoryCheck
{
    public string Id => "commits.setup";

    public string Group => "commits";

    /// <summary>Clone-local Git settings are not tracked content, so nothing here can be staged.</summary>
    public IReadOnlyList<EvidenceFile> Evidence => [];

    public string Summary => "clone-local commit message hook and template";

    public string Explanation =>
        """
        Rationale
          A tracked commit policy is only early feedback when the current clone activates it.
          Agents and disposable sandboxes are especially likely to miss a one-time manual step,
          so the repository can require setup and make an unprepared clone visible in `check`.

        What it reads
          The clone-local core.hooksPath and commit.template settings, the managed files under
          Git's metadata directory, and the harness the hook resolves at commit time: the
          clone-local <git-common-dir>/harness/bin/harness first, then `harness` on PATH. It
          does not inspect global or system Git settings.

        What it proves
          The selected language template and the managed commit-msg hook are active, and the
          harness they will run exists and is the release the frame pins. The hook validates
          the message before Git creates the commit, and refuses the commit when it resolves
          no harness at all, so a half-installed clone cannot pass silently.

        Remediation
          Run `harness setup` from anywhere inside the repository; it is idempotent, updates a
          hook an older release wrote, and refuses to replace an unrelated hooks path, commit
          template or unmanaged file. When the finding names a missing or mismatched binary,
          install the pinned release for the clone with install.sh --scope clone, or keep it on
          PATH. The hook itself holds no binary path, so no clone or worktree can be poisoned by
          the binary that ran setup.
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        if (context.Config is null)
        {
            return CheckEvaluation.Incomplete(
                "commit setup cannot be established because the harness frame is unavailable: "
                + context.ConfigFailure);
        }

        var settings = context.Config.Settings.Commits;
        if (!settings.RequireSetup)
        {
            return CheckEvaluation.NotApplicable(
                ".harness.json does not require clone-local commit setup.");
        }

        var (status, failure) = integration.Inspect(
            context.Repository,
            settings,
            CommitTemplate.Render(settings),
            context.Config.TracksLatest ? null : context.Config.Version);
        if (status is null)
        {
            return CheckEvaluation.Incomplete(failure!);
        }

        return status.Ready
            ? CheckEvaluation.Passed(status.Description)
            : CheckEvaluation.From(
                [new Finding(FindingSeverity.Blocking, ".git/config", status.Description + "; run `harness setup`.")]);
    }
}

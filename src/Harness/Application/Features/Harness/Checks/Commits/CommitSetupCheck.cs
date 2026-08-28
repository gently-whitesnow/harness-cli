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
          The clone-local core.hooksPath and commit.template settings, plus the managed files
          under Git's metadata directory. It does not inspect global or system Git settings.

        What it proves
          The selected language template and commit-msg hook installed by this harness version
          are active. The hook validates the message before Git creates the commit.

        Remediation
          Run `harness setup` from anywhere inside the repository. The command is idempotent and
          refuses to replace an unrelated hooks path or commit template.
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
            CommitTemplate.Render(settings));
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

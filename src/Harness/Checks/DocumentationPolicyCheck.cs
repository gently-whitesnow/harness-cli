using Harness.Git;

namespace Harness.Checks;

/// <summary>
/// Audits the Markdown documentation policy of a repository. Tracked state comes from
/// the Git index, so generated, vendored and build-output content stays out of scope and
/// symbolic links are judged by what Git actually stores.
/// </summary>
internal sealed class DocumentationPolicyCheck : IRepositoryCheck
{
    private const int LineLimit = DocumentationPolicyExplanation.LineLimit;
    private const string RootDocument = "ROOT.md";
    private const string ReadmeDocument = "README.md";
    private const string AdrDirectory = "adrs/";

    private static readonly string[] AgentEntryPoints = ["AGENTS.md", "CLAUDE.md"];

    public string Id => "docs.policy";

    public string Group => "docs";

    public string Summary => "Markdown documentation policy";

    public string Explanation => DocumentationPolicyExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var repository = context.Repository;

        var audit = new DocumentationAudit(repository);

        audit.RequireRootDocument();
        audit.AllowReadme();

        foreach (var entryPoint in AgentEntryPoints)
        {
            audit.RequireAgentEntryPoint(entryPoint);
        }

        audit.ReportUnexpectedMarkdown();

        return audit.Conclusion();
    }

    private static bool IsMarkdown(string path)
        => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static bool IsAllowed(string path)
        => path is RootDocument or ReadmeDocument
            || AgentEntryPoints.Contains(path, StringComparer.Ordinal)
            || path.StartsWith(AdrDirectory, StringComparison.Ordinal);

    /// <summary>
    /// One evaluation of one repository: the tracked inventory, the findings collected so
    /// far, and the first evidence gap that made the audit unreliable.
    /// </summary>
    private sealed class DocumentationAudit(GitRepository repository)
    {
        private readonly Dictionary<string, TrackedEntry> tracked =
            repository.TrackedEntries.ToDictionary(entry => entry.Path, StringComparer.Ordinal);

        private readonly List<Finding> findings = [];

        private string? evidenceGap;

        public CheckEvaluation Conclusion()
            => evidenceGap is not null
                ? CheckEvaluation.Incomplete(evidenceGap)
                : CheckEvaluation.From(findings);

        public void RequireRootDocument()
        {
            if (!tracked.TryGetValue(RootDocument, out var entry))
            {
                Violation(RootDocument, "required canonical root instruction document is not tracked by Git");
                return;
            }

            EnforceLineLimit(entry);
        }

        public void AllowReadme()
        {
            if (tracked.TryGetValue(ReadmeDocument, out var entry))
            {
                EnforceLineLimit(entry);
            }
        }

        public void RequireAgentEntryPoint(string path)
        {
            if (!tracked.TryGetValue(path, out var entry))
            {
                Violation(path, $"required Git symbolic link to {RootDocument} is not tracked by Git");
                return;
            }

            if (!entry.IsSymbolicLink)
            {
                Violation(
                    path,
                    $"is a tracked regular file instead of a symbolic link to {RootDocument}; "
                        + "a copy cannot stay synchronized");
                return;
            }

            var (target, failure) = repository.ReadSymbolicLinkTarget(entry);
            if (target is null)
            {
                RecordEvidenceGap(failure);
                return;
            }

            ClassifyLinkTarget(path, target);
        }

        public void ReportUnexpectedMarkdown()
        {
            foreach (var entry in repository.TrackedEntries)
            {
                if (!IsMarkdown(entry.Path)
                    || IsAllowed(entry.Path)
                    || RepositoryLocations.IsGenerated(entry.Path))
                {
                    continue;
                }

                findings.Add(new Finding(
                    FindingSeverity.Advisory,
                    entry.Path,
                    "unexpected tracked Markdown; remove it, fold navigation into ROOT.md, "
                        + "or move durable rationale to adrs/"));
            }
        }

        private void ClassifyLinkTarget(string path, string target)
        {
            if (target.Length == 0)
            {
                Violation(path, "symbolic link has an empty target");
                return;
            }

            if (Path.IsPathRooted(target))
            {
                Violation(
                    path,
                    $"is an absolute symbolic link to '{target}'; "
                        + $"the target must be the relative path {RootDocument}");
                return;
            }

            var normalizedTarget = target.StartsWith("./", StringComparison.Ordinal) ? target[2..] : target;
            if (string.Equals(normalizedTarget, RootDocument, StringComparison.Ordinal))
            {
                return;
            }

            if (!tracked.TryGetValue(normalizedTarget, out var targetEntry))
            {
                Violation(path, $"is a broken symbolic link: '{target}' is not tracked by Git");
                return;
            }

            Violation(
                path,
                targetEntry.IsSymbolicLink
                    ? $"is a chained symbolic link through '{target}'; it must point directly at {RootDocument}"
                    : $"points at '{target}' instead of {RootDocument}");
        }

        private void EnforceLineLimit(TrackedEntry entry)
        {
            var (text, failure) = repository.ReadTrackedText(entry);
            if (text is null)
            {
                RecordEvidenceGap(failure);
                return;
            }

            var lineCount = CountPhysicalLines(text);
            if (lineCount > LineLimit)
            {
                Violation(entry.Path, $"{lineCount} physical lines exceeds the limit of {LineLimit}");
            }
        }

        private static int CountPhysicalLines(string text)
        {
            if (text.Length == 0)
            {
                return 0;
            }

            var lineCount = text.Count(character => character == '\n');

            // A final line without a trailing newline is still a physical line.
            return text[^1] == '\n' ? lineCount : lineCount + 1;
        }

        private void Violation(string location, string message)
            => findings.Add(new Finding(FindingSeverity.Blocking, location, message));

        private void RecordEvidenceGap(string? failure)
            => evidenceGap ??= failure ?? "Git evidence could not be read.";
    }
}

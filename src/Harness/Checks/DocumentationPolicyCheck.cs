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
    private const string RootDocument = "AGENTS.md";
    private const string ReadmeDocument = "README.md";
    private const string AgentEntryPoint = "CLAUDE.md";
    private const string SkillDocument = "SKILL.md";
    private const string AdrDirectory = "adrs/";

    public string Id => "docs.policy";

    public string Group => "docs";

    /// <summary>The four names this policy knows; the rest of the Markdown is judged as a corpus.</summary>
    public IReadOnlyList<EvidenceFile> Evidence =>
        [new(RootDocument), new(AgentEntryPoint), new(ReadmeDocument), new(SkillDocument)];

    public string Summary => "Markdown documentation policy";

    public string Explanation => DocumentationPolicyExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var audit = new DocumentationAudit(context.Repository);

        audit.RequireRootDocument();
        audit.RequireRootEntryPoint();
        audit.ReviewRemainingMarkdown();

        return audit.Conclusion();
    }

    private static bool IsMarkdown(string path)
        => path.EndsWith(".md", StringComparison.OrdinalIgnoreCase);

    private static string FileNameOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? path : path[(separator + 1)..];
    }

    private static string DirectoryOf(string path)
    {
        var separator = path.LastIndexOf('/');
        return separator < 0 ? string.Empty : path[..(separator + 1)];
    }

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

            ReviewInstructionDocument(entry);
        }

        public void RequireRootEntryPoint()
        {
            if (!tracked.TryGetValue(AgentEntryPoint, out var entry))
            {
                Violation(AgentEntryPoint, $"required Git symbolic link to {RootDocument} is not tracked by Git");
                return;
            }

            ReviewEntryPoint(entry);
        }

        /// <summary>
        /// Judges every tracked Markdown document the root rules did not already own. The
        /// three cross-vendor names an agent opens by itself keep their meaning at any
        /// depth, so the directory a document lives in decides nothing on its own.
        /// </summary>
        public void ReviewRemainingMarkdown()
        {
            foreach (var entry in repository.TrackedEntries)
            {
                if (!IsMarkdown(entry.Path)
                    || entry.Path is RootDocument or AgentEntryPoint
                    || entry.Path.StartsWith(AdrDirectory, StringComparison.Ordinal)
                    || RepositoryLocations.IsGenerated(entry.Path))
                {
                    continue;
                }

                Review(entry);
            }
        }

        private void Review(TrackedEntry entry)
        {
            switch (FileNameOf(entry.Path))
            {
                case RootDocument:
                    ReviewInstructionDocument(entry);
                    return;

                case AgentEntryPoint:
                    ReviewEntryPoint(entry);
                    return;

                case ReadmeDocument:
                    EnforceLineLimit(entry);
                    return;

                // An agent skill is a payload loaded on demand for one task, not navigation
                // carried in every context, so the navigation line limit does not apply.
                case SkillDocument:
                    return;

                default:
                    Violation(
                        entry.Path,
                        "unexpected tracked Markdown; remove it, fold navigation into AGENTS.md, "
                            + "or move durable rationale to adrs/");
                    return;
            }
        }

        private void ReviewInstructionDocument(TrackedEntry entry)
        {
            if (entry.IsSymbolicLink)
            {
                Violation(
                    entry.Path,
                    "is a tracked symbolic link instead of the canonical document itself; "
                        + "the agent entry points link to it, not the other way round");
                return;
            }

            EnforceLineLimit(entry);
        }

        private void ReviewEntryPoint(TrackedEntry entry)
        {
            if (!entry.IsSymbolicLink)
            {
                Violation(
                    entry.Path,
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

            ClassifyLinkTarget(entry.Path, target);
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

            var directory = DirectoryOf(path);
            var resolved = Resolve(directory, target);
            if (resolved is null)
            {
                Violation(path, $"is a symbolic link to '{target}', which climbs outside the repository");
                return;
            }

            if (string.Equals(resolved, directory + RootDocument, StringComparison.Ordinal))
            {
                RequireLinkedDocument(path, resolved);
                return;
            }

            if (!tracked.TryGetValue(resolved, out var targetEntry))
            {
                Violation(path, $"is a broken symbolic link: '{target}' is not tracked by Git");
                return;
            }

            Violation(
                path,
                targetEntry.IsSymbolicLink
                    ? $"is a chained symbolic link through '{target}'; it must point directly at {RootDocument}"
                    : $"points at '{target}' instead of the {RootDocument} beside it");
        }

        private void RequireLinkedDocument(string path, string resolved)
        {
            // The root pair is judged by RequireRootDocument, which already names a missing
            // root; only a nested entry point can point at a sibling that is not there.
            if (resolved != RootDocument && !tracked.ContainsKey(resolved))
            {
                Violation(path, $"is a broken symbolic link: no {RootDocument} is tracked beside it");
            }
        }

        /// <summary>
        /// Resolves a relative link target against the directory holding the link, so a
        /// target that climbs is compared as the path Git would actually follow.
        /// </summary>
        private static string? Resolve(string directory, string target)
        {
            List<string> segments = directory.Length == 0
                ? []
                : [.. directory.TrimEnd('/').Split('/')];

            foreach (var segment in target.Split('/'))
            {
                if (segment.Length == 0 || segment == ".")
                {
                    continue;
                }

                if (segment != "..")
                {
                    segments.Add(segment);
                    continue;
                }

                if (segments.Count == 0)
                {
                    return null;
                }

                segments.RemoveAt(segments.Count - 1);
            }

            return string.Join('/', segments);
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

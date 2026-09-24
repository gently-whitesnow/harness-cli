using System.Globalization;
using System.Text.RegularExpressions;
using Harness.Config;
using Harness.Repository;

namespace Harness.Checks;

/// <summary>Checks the current tracked ADR catalogue as decision records.</summary>
internal sealed partial class AdrShapeCheck : IRepositoryCheck
{
    public string Id => "adrs.shape";
    public string Group => "adrs";
    public IReadOnlyList<EvidenceFile> Evidence => [new("*.md")];
    public string Summary => "ADR decision record shape";
    public string Explanation => """
        Rationale
          ADRs record decisions. Numbering, status, date and a small shared structure keep
          an ADR discoverable without turning the catalogue into a mutable specification.

        Rules
          Each tracked adrs/*.md except REGISTRY.md and .template.md has a four-digit
          NNNN-kebab-case.md name. Numbers start at 0001, are unique and have no gaps.
          Restore a removed number as a short Rejected, Deprecated or Superseded record;
          do not renumber later decisions. The header needs a date (YYYY-MM-DD) and a
          status beginning Proposed, Accepted, Rejected, Deprecated, Superseded or
          Partially superseded, or their Russian forms. An ADR-N reference in the status
          must name an existing number. H2 sections Context, Decision and Consequences
          (or Контекст, Решение, Последствия) are required; only Alternatives/Альтернативы
          and References/Ссылки are additionally allowed. H3 and below are unrestricted.
          Settings in settings.adrs.shape declare wordLimit (default 1000),
          fencedLineLimit (default 10 total code lines) and tableRowLimit (default 12
          rows in one table). Only the tracked frame controls these numbers.

        Remediation
          Summarize the decision and its consequences; move implementation detail into
          code or a purpose-built document. Preserve an old ADR when superseding it.
        """;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var settings = context.Config?.Settings.AdrShape;
        if (settings is null)
        {
            return CheckEvaluation.Incomplete("settings.adrs.shape is missing");
        }

        var markdown = context.Tracked(new EvidenceFile("*.md"))
            .Where(entry => entry.Path.StartsWith("adrs/", StringComparison.Ordinal))
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();
        var findings = new List<Finding>();
        foreach (var entry in markdown.Where(entry => entry.Path.AsSpan(5).IndexOf('/') >= 0))
        {
            findings.Add(new Finding(FindingSeverity.Blocking, entry.Path, "ADR Markdown must live directly under adrs/"));
        }

        var entries = markdown.Where(entry => entry.Path.AsSpan(5).IndexOf('/') < 0
            && entry.Path is not ("adrs/REGISTRY.md" or "adrs/.template.md")).ToList();
        var numbered = new Dictionary<int, string>();
        foreach (var entry in entries)
        {
            var name = entry.Path[5..];
            var match = NamePattern().Match(name);
            if (!match.Success)
            {
                Add(entry.Path, "name must be NNNN-kebab-case.md (four digits)");
                continue;
            }

            var number = int.Parse(match.Groups[1].Value, System.Globalization.CultureInfo.InvariantCulture);
            if (number == 0)
            {
                Add(entry.Path, "ADR numbers start at 0001");
            }
            else if (!numbered.TryAdd(number, entry.Path))
            {
                Add(entry.Path, $"ADR-{number:0000} duplicates {numbered[number]}; assign the next free number and update its references");
            }
        }

        if (numbered.Count > 0)
        {
            foreach (var number in Enumerable.Range(1, numbered.Keys.Max()).Where(number => !numbered.ContainsKey(number)))
            {
                Add("adrs/", $"ADR-{number:0000} is missing; restore a decision record with Rejected, Deprecated or Superseded status, rather than renumbering later ADRs");
            }
        }

        var numbers = numbered.Keys.ToHashSet();
        foreach (var entry in entries)
        {
            var (source, failure) = context.Repository.ReadTrackedText(entry);
            if (source is null)
            {
                return CheckEvaluation.Incomplete(failure ?? $"Could not read {entry.Path}", findings);
            }

            Review(entry.Path, source, settings, numbers, Add);
        }

        return CheckEvaluation.From(findings);

        void Add(string path, string message) => findings.Add(new Finding(FindingSeverity.Blocking, path, message));
    }

    private static void Review(string path, string source, AdrShapeSettings settings, HashSet<int> numbers, Action<string, string> add)
    {
        var (header, sections, status, fencedLines, tableMaximum) = Scan(path, source, add);

        if (!StatusPattern().IsMatch(status))
        {
            add(path, "header needs a recognized status beginning Proposed, Accepted, Rejected, Deprecated, Superseded or Partially superseded (Russian forms allowed)");
        }

        if (!header.SelectMany(line => DatePattern().Matches(line).Select(match => match.Value))
            .Any(value => DateOnly.TryParseExact(value, "yyyy-MM-dd", CultureInfo.InvariantCulture, DateTimeStyles.None, out _)))
        {
            add(path, "header needs a date in YYYY-MM-DD form that is valid");
        }

        foreach (Match reference in AdrReference().Matches(status))
        {
            if (int.TryParse(reference.Groups[1].Value, out var number) && !numbers.Contains(number))
            {
                add(path, $"status references missing ADR-{number:0000}");
            }
        }

        foreach (var required in new[] { ("Context", "Контекст"), ("Decision", "Решение"), ("Consequences", "Последствия") })
        {
            if (!sections.Contains(required.Item1) && !sections.Contains(required.Item2))
            {
                add(path, $"missing H2 section {required.Item1}/{required.Item2}");
            }
        }

        var words = WordPattern().Count(source);
        if (words > settings.WordLimit)
        {
            add(path, $"{words} words exceeds wordLimit {settings.WordLimit}; summarize or split the decision");
        }

        if (fencedLines > settings.FencedLineLimit)
        {
            add(path, $"{fencedLines} fenced lines exceeds fencedLineLimit {settings.FencedLineLimit}; keep code in source files");
        }

        if (tableMaximum > settings.TableRowLimit)
        {
            add(path, $"table has {tableMaximum} rows, above tableRowLimit {settings.TableRowLimit}; summarize the comparison");
        }
    }

    private sealed record AdrScan(List<string> Header, HashSet<string> Sections, string Status, int FencedLines, int TableMaximum);

    private static AdrScan Scan(string path, string source, Action<string, string> add)
    {
        var lines = source.Split('\n');
        var sections = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var header = new List<string>();
        var inFence = false;
        var fenceMarker = '\0';
        var fencedLines = 0;
        var tableRows = 0;
        var tableMaximum = 0;
        var headerEnded = false;
        var statusSection = false;
        var status = string.Empty;

        foreach (var raw in lines)
        {
            var line = raw.Trim();
            if (line.StartsWith("```", StringComparison.Ordinal) || line.StartsWith("~~~", StringComparison.Ordinal))
            {
                if (!inFence)
                {
                    inFence = true;
                    fenceMarker = line[0];
                }
                else if (line[0] == fenceMarker)
                {
                    inFence = false;
                }

                continue;
            }

            if (inFence)
            {
                fencedLines++;
                continue;
            }

            if (line.StartsWith("## ", StringComparison.Ordinal))
            {
                var section = line[3..].Trim();
                if (!headerEnded && section is ("Status" or "Статус"))
                {
                    statusSection = true;
                    continue;
                }

                headerEnded = true;
                statusSection = false;
                if (!AllowedSections.Contains(section))
                {
                    add(path, $"unexpected H2 section '{section}'; use Context, Decision, Consequences, Alternatives or References (Russian equivalents allowed)");
                }
                else
                {
                    sections.Add(section);
                }

                continue;
            }

            if (!headerEnded)
            {
                header.Add(line);
                status = StatusOnLine(line, statusSection, status);
            }

            if (line.Contains('|') && line.StartsWith('|') && line.EndsWith('|'))
            {
                if (!TableDivider().IsMatch(line))
                {
                    tableRows++;
                    tableMaximum = Math.Max(tableMaximum, tableRows);
                }
            }
            else
            {
                tableRows = 0;
            }
        }

        if (status.Length == 0)
        {
            status = header.Select(line => line.StartsWith("Status:", StringComparison.OrdinalIgnoreCase)
                ? line[7..].Trim() : line.StartsWith("Статус:", StringComparison.OrdinalIgnoreCase)
                    ? line[7..].Trim() : string.Empty).FirstOrDefault(line => line.Length > 0) ?? string.Empty;
        }

        return new AdrScan(header, sections, status, fencedLines, tableMaximum);
    }

    private static string StatusOnLine(string line, bool statusSection, string status)
        => statusSection && status.Length == 0 && line.Length > 0
            && !line.StartsWith("Date:", StringComparison.OrdinalIgnoreCase)
            && !line.StartsWith("Дата:", StringComparison.OrdinalIgnoreCase)
                ? line : status;

    private static readonly HashSet<string> AllowedSections = new(StringComparer.OrdinalIgnoreCase)
    {
        "Context", "Decision", "Consequences", "Alternatives", "References",
        "Контекст", "Решение", "Последствия", "Альтернативы", "Ссылки",
    };

    [GeneratedRegex(@"^(\d{4})-[a-z0-9]+(?:-[a-z0-9]+)*\.md$", RegexOptions.CultureInvariant)]
    private static partial Regex NamePattern();
    [GeneratedRegex(@"^(?:Proposed|Accepted|Rejected|Deprecated|Superseded|Partially superseded|Предложен[аоы]?|Принят[аоы]?|Отклон[её]н[аоы]?|Устарел[аоы]?|Замен[её]н[аоы]?|Частично замен[её]н[аоы]?)(?:\b|[.:])", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex StatusPattern();
    [GeneratedRegex(@"\b\d{4}-\d{2}-\d{2}\b", RegexOptions.CultureInvariant)]
    private static partial Regex DatePattern();
    [GeneratedRegex(@"\bADR-(\d+)\b", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex AdrReference();
    [GeneratedRegex(@"\S+", RegexOptions.CultureInvariant)]
    private static partial Regex WordPattern();
    [GeneratedRegex(@"^\|[\s:|\-]+\|$", RegexOptions.CultureInvariant)]
    private static partial Regex TableDivider();
}

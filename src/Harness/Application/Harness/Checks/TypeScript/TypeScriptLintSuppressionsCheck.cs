using System.Text.RegularExpressions;
using Harness.Languages;
using Harness.Repository;

namespace Harness.Checks.TypeScript;

/// <summary>
/// A typechecker or linter only protects a TypeScript/JavaScript codebase while diagnostics
/// are not silenced line by line. This check scans tracked comments for `@ts-ignore`,
/// `@ts-nocheck`, `@ts-expect-error`, `eslint-disable*`, `biome-ignore` and `oxlint-disable*`
/// without locating or running any external toolchain.
/// </summary>
internal sealed partial class TypeScriptLintSuppressionsCheck : IRepositoryCheck
{
    private static readonly IReadOnlyList<EvidenceFile> Sources =
        Language.TypeScript.Suffixes.Select(suffix => new EvidenceFile($"*{suffix}")).ToList();

    public string Id => Language.TypeScript.Qualify(Group);

    public string Group => "lint-suppressions";

    public string Applicability => Language.TypeScript.Key;

    public IReadOnlyList<EvidenceFile> Evidence => Sources;

    public string Summary => "TypeScript and JS linters are not silenced at an address";

    public string Explanation => TypeScriptLintSuppressionsExplanation.Text;

    public CheckEvaluation Evaluate(CheckContext context)
    {
        var files = Sources
            .SelectMany(context.Tracked)
            .DistinctBy(entry => entry.Path, StringComparer.Ordinal)
            .OrderBy(entry => entry.Path, StringComparer.Ordinal)
            .ToList();

        if (files.Count == 0)
        {
            return CheckEvaluation.NotApplicable("no TypeScript or JavaScript sources in the repository");
        }

        var findings = new List<Finding>();
        foreach (var entry in files)
        {
            var (text, failure) = context.Repository.ReadTrackedText(entry);
            if (text is null)
            {
                return CheckEvaluation.Incomplete(failure ?? $"Could not read '{entry.Path}'.");
            }

            ScanComments(entry.Path, text, findings);
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "no TypeScript or linter diagnostic is silenced at an address" : null);
    }

    private static void ScanComments(string path, string text, List<Finding> findings)
    {
        var line = 1;
        var index = 0;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\n')
            {
                line++;
                index++;
                continue;
            }

            if (ch is '\'' or '"' or '`')
            {
                index = SkipString(text, index, ch, ref line);
                continue;
            }

            if (ch == '/' && index + 1 < text.Length && text[index + 1] == '/')
            {
                var start = index + 2;
                var end = text.IndexOf('\n', start);
                if (end < 0)
                {
                    end = text.Length;
                }

                CheckComment(path, line, text[start..end], findings);
                index = end;
                continue;
            }

            if (ch == '/' && index + 1 < text.Length && text[index + 1] == '*')
            {
                var startLine = line;
                var start = index + 2;
                var close = text.IndexOf("*/", start, StringComparison.Ordinal);
                var end = close < 0 ? text.Length : close;
                var content = text[start..end];
                CheckComment(path, startLine, content, findings);
                foreach (var c in content)
                {
                    if (c == '\n')
                    {
                        line++;
                    }
                }

                index = close < 0 ? text.Length : close + 2;
                continue;
            }

            index++;
        }
    }

    private static int SkipString(string text, int start, char quote, ref int line)
    {
        var index = start + 1;
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '\\')
            {
                index += 2;
                continue;
            }

            if (ch == '\n')
            {
                line++;
                if (quote != '`')
                {
                    return index + 1;
                }
            }
            else if (ch == quote)
            {
                return index + 1;
            }

            index++;
        }

        return text.Length;
    }

    private static void CheckComment(string path, int startLine, string content, List<Finding> findings)
    {
        var lines = content.Split('\n');
        for (var offset = 0; offset < lines.Length; offset++)
        {
            var match = SuppressionDirective().Match(lines[offset]);
            if (match.Success)
            {
                findings.Add(new Finding(
                    FindingSeverity.Blocking,
                    $"{path}:{startLine + offset}",
                    $"silences TypeScript/linter diagnostics via {match.Value}; fix the code instead of suppressing at an address"));
            }
        }
    }

    [GeneratedRegex(
        @"(?<!\w)(@ts-ignore|@ts-nocheck|@ts-expect-error|eslint-disable(?:-next-line|-line)?|biome-ignore|oxlint-disable(?:-next-line|-line)?)(?!\w)",
        RegexOptions.CultureInvariant)]
    private static partial Regex SuppressionDirective();
}

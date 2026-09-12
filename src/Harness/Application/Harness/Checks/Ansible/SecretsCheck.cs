using System.Text.RegularExpressions;
using Harness.Languages.Ansible;

namespace Harness.Checks.Ansible;

/// <summary>
/// A secret in a variables file is stored in one of three forms — a `!vault` block, a Jinja
/// lookup, or nothing — and never as a literal. The check asks for the form under names that
/// obviously hold one; it does not search for secrets, and a key outside its dictionary is
/// not judged.
/// </summary>
internal sealed partial class SecretsCheck(IAnsibleSources sources)
    : AnsibleSourceCheck(sources, "secrets", "secrets in variables are vaulted or looked up")
{
    private const int MinimumLiteralLength = 8;

    private static readonly string[] Names =
    [
        "password", "passwd", "secret", "secret_id", "token", "api_key", "apikey",
        "access_key", "private_key", "privatekey", "client_secret",
    ];

    private static readonly string[] ExactExclusions = ["public_key", "pubkey", "token_file", "token_ttl"];

    private static readonly string[] SuffixExclusions = ["_key_file", "_key_path", "_key_name", "_keys", "_ttl"];

    public override string Explanation => SecretsExplanation.Text;

    protected override CheckEvaluation Judge(CheckContext context, IReadOnlyList<AnsibleFile> files)
    {
        var findings = new List<Finding>();
        foreach (var file in files.Where(file => file.DeclaresVariables))
        {
            foreach (var line in file.Lines.Where(line => line.Key is not null && !line.IsOpaque && IsSecretName(line.Key) && IsLiteral(line.Scalar)))
            {
                findings.Add(new Finding(
                    FindingSeverity.Blocking,
                    $"{file.Path}:{line.Number}",
                    $"`{line.Key}` holds a literal value in a variables file; store it as a `!vault` block, "
                        + "resolve it with a lookup at run time, or leave it empty for the host to provide"));
            }
        }

        return CheckEvaluation.From(findings, findings.Count == 0 ? "no secret-named variable holds a literal" : null);
    }

    /// <summary>The dictionary, by the whole name or by contiguous `_`/`-` segments, minus the names that only sound like a secret.</summary>
    public static bool IsSecretName(string key)
    {
        var name = key.ToLowerInvariant();
        if (ExactExclusions.Contains(name, StringComparer.Ordinal)
            || SuffixExclusions.Any(suffix => name.EndsWith(suffix, StringComparison.Ordinal)))
        {
            return false;
        }

        var segments = name.Split(['_', '-'], StringSplitOptions.RemoveEmptyEntries);
        return Names.Any(entry => ContainsRun(segments, entry.Split('_')));
    }

    private static bool ContainsRun(string[] segments, string[] run)
    {
        for (var start = 0; start + run.Length <= segments.Length; start++)
        {
            if (run.SequenceEqual(segments.Skip(start).Take(run.Length), StringComparer.Ordinal))
            {
                return true;
            }
        }

        return false;
    }

    // A literal is long enough to be a credential and is not a vault block, a Jinja expression,
    // a null, a flow collection, or a snake_case identifier naming a field elsewhere.
    private static bool IsLiteral(string value)
        => value.Length >= MinimumLiteralLength
            && !value.Contains("{{", StringComparison.Ordinal)
            && !value.StartsWith("!vault", StringComparison.Ordinal)
            && value is not ("null" or "~")
            && value[0] is not ('[' or '{')
            && !Identifier().IsMatch(value);

    [GeneratedRegex("^[A-Za-z][A-Za-z0-9]*(_[A-Za-z0-9]+)+$", RegexOptions.CultureInvariant)]
    private static partial Regex Identifier();
}

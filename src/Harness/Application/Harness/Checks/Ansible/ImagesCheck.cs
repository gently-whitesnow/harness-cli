using System.Text.RegularExpressions;
using Harness.Languages.Ansible;
using Harness.Repository;

namespace Harness.Checks.Ansible;

/// <summary>
/// A container image named by tag can change under a deployment; one named by its full digest
/// cannot. Every `image:` scalar in tracked YAML and Jinja templates must end in the digest.
/// A value written in Jinja is resolved by Ansible at run time and is printed, not judged.
/// </summary>
internal sealed partial class ImagesCheck(IAnsibleSources sources)
    : AnsibleSourceCheck(sources, "images", "container images are pinned by digest")
{
    private const string ImageKey = "image";

    public override IReadOnlyList<EvidenceFile> Evidence => [.. AnsibleMarkers.Sources, new("*.j2")];

    public override string Explanation => ImagesExplanation.Text;

    protected override CheckEvaluation Judge(CheckContext context, IReadOnlyList<AnsibleFile> files)
    {
        var findings = new List<Finding>();
        var details = new List<string>();
        foreach (var file in files)
        {
            foreach (var line in file.Lines.Where(line => line.Key == ImageKey))
            {
                var location = $"{file.Path}:{line.Number}";
                var image = line.Scalar;
                if (line.IsJinja)
                {
                    details.Add($"{location}: image `{image}` is resolved by Ansible at run time (Inferred); the variable is not traced; only literal image keys are checked");
                }
                else if (line.IsOpaque)
                {
                    details.Add($"{location}: image uses an anchor, alias or flow collection (Inferred); lexical reading cannot prove a digest");
                }
                else if (!Digest().IsMatch(image))
                {
                    findings.Add(new Finding(FindingSeverity.Blocking, location, Describe(image)));
                }
            }
        }

        return CheckEvaluation.From(
            findings,
            findings.Count == 0 ? "every literal image is pinned by a full digest" : null,
            details: details);
    }

    private static string Describe(string image)
    {
        var shape = image.Length == 0
            ? "has no value"
            : image.Contains("@sha256:", StringComparison.Ordinal)
                ? $"`{image}` carries an incomplete digest"
                : $"`{image}` is named by tag, not by digest";
        return $"image {shape}; pin it as `<name>@sha256:<64 hex digits>` so the deployed artifact cannot change under the deployment";
    }

    [GeneratedRegex("@sha256:[0-9a-f]{64}$", RegexOptions.CultureInvariant)]
    private static partial Regex Digest();
}

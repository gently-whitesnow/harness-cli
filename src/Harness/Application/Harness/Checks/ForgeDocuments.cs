namespace Harness.Checks;

/// <summary>
/// The Markdown a forge reads by name and place: community health files and issue or
/// pull-request templates. Their names, locations and shape are dictated by the forge, so
/// the policy recognizes them instead of asking every repository to configure the same list.
/// </summary>
internal static class ForgeDocuments
{
    /// <summary>Community health files GitHub and GitLab surface in their own interface.</summary>
    public static readonly IReadOnlyList<string> CommunityNames =
    [
        "CHANGELOG.md",
        "CODE_OF_CONDUCT.md",
        "CONTRIBUTING.md",
        "GOVERNANCE.md",
        "SECURITY.md",
        "SUPPORT.md",
    ];

    /// <summary>The only directories where a forge looks for a community file or a template.</summary>
    public static readonly IReadOnlyList<string> Locations = [string.Empty, ".github/", "docs/"];

    private static readonly string[] SingleTemplates = ["pull_request_template.md", "issue_template.md"];

    private static readonly string[] TemplateDirectories =
    [
        ".github/ISSUE_TEMPLATE/",
        ".github/PULL_REQUEST_TEMPLATE/",
        "PULL_REQUEST_TEMPLATE/",
        "docs/PULL_REQUEST_TEMPLATE/",
        ".gitlab/issue_templates/",
        ".gitlab/merge_request_templates/",
    ];

    /// <summary>Whether a forge would pick this tracked Markdown path up by itself. Forges
    /// match these names without regard to case, so the policy does too.</summary>
    public static bool Recognizes(string path)
    {
        var separator = path.LastIndexOf('/');
        var directory = path[..(separator + 1)];
        var name = path[(separator + 1)..];

        if (Locations.Contains(directory, StringComparer.OrdinalIgnoreCase))
        {
            return CommunityNames.Contains(name, StringComparer.OrdinalIgnoreCase)
                || SingleTemplates.Contains(name, StringComparer.OrdinalIgnoreCase);
        }

        return TemplateDirectories.Contains(directory, StringComparer.OrdinalIgnoreCase);
    }
}

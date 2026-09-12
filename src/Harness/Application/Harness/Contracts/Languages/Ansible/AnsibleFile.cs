namespace Harness.Languages.Ansible;

/// <summary>One tracked YAML file or Jinja template of an Ansible repository, reduced to its lines.</summary>
internal sealed record AnsibleFile(string Path, IReadOnlyList<AnsibleLine> Lines)
{
    private static readonly string[] VarsDirectories = ["group_vars", "host_vars", "vars"];

    private static readonly string[] RoleVarsParts = ["defaults", "vars"];

    public bool IsTemplate => Path.EndsWith(".j2", StringComparison.OrdinalIgnoreCase);

    /// <summary>The role this file belongs to, when it lies under `roles/[name]/`.</summary>
    public string? Role
    {
        get
        {
            var segments = Path.Split('/');
            return segments.Length > 2 && segments[0] == AnsibleMarkers.RolesDirectory ? segments[1] : null;
        }
    }

    /// <summary>The role directory the file sits in: `tasks`, `handlers`, `meta`, `defaults`, ...</summary>
    public string? RolePart
    {
        get
        {
            var segments = Path.Split('/');
            return Role is not null && segments.Length > 3 ? segments[2] : null;
        }
    }

    /// <summary>Where variables are declared: the only place a literal secret is judged.</summary>
    public bool DeclaresVariables
    {
        get
        {
            var first = Path.Split('/')[0];
            return !IsTemplate
                && (VarsDirectories.Contains(first, StringComparer.Ordinal)
                    || first.StartsWith("inventory", StringComparison.Ordinal)
                    || (RolePart is { } part && RoleVarsParts.Contains(part, StringComparer.Ordinal)));
        }
    }
}

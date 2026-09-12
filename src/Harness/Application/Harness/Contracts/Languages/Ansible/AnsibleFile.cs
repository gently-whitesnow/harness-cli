namespace Harness.Languages.Ansible;

/// <summary>One tracked YAML file of an Ansible repository, reduced to its lines.</summary>
internal sealed record AnsibleFile(string Path, IReadOnlyList<AnsibleLine> Lines)
{
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

}

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class Role
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public string Scope { get; set; } = "Organization";
    public bool IsSystemRole { get; set; }
    public DateTime CreatedAtUtc { get; set; }

    public Organization? Organization { get; set; }
    public ICollection<RolePermission> Permissions { get; set; } = new List<RolePermission>();
    public ICollection<OrganizationMemberRole> Members { get; set; } = new List<OrganizationMemberRole>();
}

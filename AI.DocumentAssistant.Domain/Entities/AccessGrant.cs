using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class AccessGrant
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public Guid? RoleId { get; set; }
    public AccessResourceType ResourceType { get; set; }
    public Guid ResourceId { get; set; }
    public string PermissionKey { get; set; } = default!;
    public Guid GrantedByUserId { get; set; }
    public DateTime GrantedAtUtc { get; set; }

    public Organization Organization { get; set; } = default!;
    public User? User { get; set; }
    public Team? Team { get; set; }
    public Role? Role { get; set; }
    public User GrantedByUser { get; set; } = default!;
}

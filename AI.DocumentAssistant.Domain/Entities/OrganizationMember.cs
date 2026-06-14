using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationMember
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public OrganizationMemberStatus Status { get; set; } = OrganizationMemberStatus.Active;
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? SuspendedAtUtc { get; set; }

    public Organization Organization { get; set; } = default!;
    public User User { get; set; } = default!;
    public ICollection<OrganizationMemberRole> Roles { get; set; } = new List<OrganizationMemberRole>();
    public ICollection<TeamMember> TeamMemberships { get; set; } = new List<TeamMember>();
}

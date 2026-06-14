namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationMemberRole
{
    public Guid OrganizationMemberId { get; set; }
    public Guid RoleId { get; set; }

    public OrganizationMember OrganizationMember { get; set; } = default!;
    public Role Role { get; set; } = default!;
}

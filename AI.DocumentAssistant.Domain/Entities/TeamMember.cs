namespace AI.DocumentAssistant.Domain.Entities;

public sealed class TeamMember
{
    public Guid TeamId { get; set; }
    public Guid OrganizationMemberId { get; set; }
    public DateTime AddedAtUtc { get; set; }

    public Team Team { get; set; } = default!;
    public OrganizationMember OrganizationMember { get; set; } = default!;
}

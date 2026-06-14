namespace AI.DocumentAssistant.Domain.Entities;

public sealed class Team
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Name { get; set; } = default!;
    public string NormalizedName { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }

    public Organization Organization { get; set; } = default!;
    public ICollection<TeamMember> Members { get; set; } = new List<TeamMember>();
    public ICollection<AccessGrant> AccessGrants { get; set; } = new List<AccessGrant>();
}

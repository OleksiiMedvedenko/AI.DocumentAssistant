namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationSettings
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public int InvitationLifetimeDays { get; set; } = 3;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }

    public Organization Organization { get; set; } = default!;
}

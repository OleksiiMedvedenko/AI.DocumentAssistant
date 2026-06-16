namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationMember
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public Guid AddedByUserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public Organization Organization { get; set; } = default!;
    public User User { get; set; } = default!;
    public User AddedByUser { get; set; } = default!;
}

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class Organization
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public Guid CreatedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsActive { get; set; } = true;

    public User CreatedByUser { get; set; } = default!;
    public OrganizationSettings? Settings { get; set; }
    public ICollection<OrganizationMember> Members { get; set; } = new List<OrganizationMember>();
    public ICollection<OrganizationInvitation> Invitations { get; set; } = new List<OrganizationInvitation>();
    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
    public ICollection<OrganizationActivityLog> ActivityLogs { get; set; } = new List<OrganizationActivityLog>();
}

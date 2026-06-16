using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationInvitation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = default!;
    public Guid InvitedUserId { get; set; }
    public Guid InvitedByUserId { get; set; }
    public string CodeHash { get; set; } = default!;
    public OrganizationInvitationStatus Status { get; set; } = OrganizationInvitationStatus.Pending;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
    public Guid? RevokedByUserId { get; set; }

    public Organization Organization { get; set; } = default!;
    public User InvitedUser { get; set; } = default!;
    public User InvitedByUser { get; set; } = default!;
    public User? RevokedByUser { get; set; }
    public ICollection<UserNotification> Notifications { get; set; } = new List<UserNotification>();
}

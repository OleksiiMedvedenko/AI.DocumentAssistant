using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationInvitation
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string TokenHash { get; set; } = default!;
    public OrganizationInvitationStatus Status { get; set; } = OrganizationInvitationStatus.Pending;
    public Guid InvitedByUserId { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public Guid? AcceptedByUserId { get; set; }

    public Organization Organization { get; set; } = default!;
    public User InvitedByUser { get; set; } = default!;
    public User? AcceptedByUser { get; set; }
}

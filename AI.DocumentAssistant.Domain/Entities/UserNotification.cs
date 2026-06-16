using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class UserNotification
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public UserNotificationType Type { get; set; }
    public string TitleKey { get; set; } = default!;
    public string MessageKey { get; set; } = default!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public DateTime? DismissedAtUtc { get; set; }
    public Guid? RelatedOrganizationId { get; set; }
    public Guid? RelatedInvitationId { get; set; }

    public User User { get; set; } = default!;
    public Organization? RelatedOrganization { get; set; }
    public OrganizationInvitation? RelatedInvitation { get; set; }
}

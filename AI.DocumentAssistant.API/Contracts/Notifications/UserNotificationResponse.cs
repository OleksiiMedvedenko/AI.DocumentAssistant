namespace AI.DocumentAssistant.API.Contracts.Notifications;

public sealed class UserNotificationResponse
{
    public Guid Id { get; set; }
    public string Type { get; set; } = default!;
    public string TitleKey { get; set; } = default!;
    public string MessageKey { get; set; } = default!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? ReadAtUtc { get; set; }
    public Guid? RelatedOrganizationId { get; set; }
    public Guid? RelatedInvitationId { get; set; }
}

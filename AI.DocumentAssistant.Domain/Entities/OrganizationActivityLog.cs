using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class OrganizationActivityLog
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public OrganizationActivityActionType ActionType { get; set; }
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }

    public Organization Organization { get; set; } = default!;
    public User? ActorUser { get; set; }
}

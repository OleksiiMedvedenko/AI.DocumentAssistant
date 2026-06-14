using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Domain.Entities;

public sealed class AuditLog
{
    public Guid Id { get; set; }
    public Guid? OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public AuditLogAction Action { get; set; }
    public AccessResourceType? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }

    public Organization? Organization { get; set; }
    public User? ActorUser { get; set; }
}

namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class OrganizationActivityLogResponse
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public string ActionType { get; set; } = default!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}

namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class OrganizationActivityLogDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid? ActorUserId { get; set; }
    public string? ActorEmail { get; set; }
    public string ActionType { get; set; } = default!;
    public string PayloadJson { get; set; } = "{}";
    public DateTime CreatedAtUtc { get; set; }
}

namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class OrganizationMemberDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string UserRole { get; set; } = default!;
    public Guid AddedByUserId { get; set; }
    public DateTime JoinedAtUtc { get; set; }
    public DateTime? RemovedAtUtc { get; set; }
    public bool IsActive { get; set; }
}

namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class OrganizationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public Guid CreatedByUserId { get; set; }
    public string CreatedByEmail { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime? UpdatedAtUtc { get; set; }
    public bool IsActive { get; set; }
    public int ActiveMembersCount { get; set; }
    public bool CanManage { get; set; }
}

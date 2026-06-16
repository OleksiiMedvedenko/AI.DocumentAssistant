namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class AddOrganizationMemberDto
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
}

namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class OrganizationSettingsDto
{
    public Guid OrganizationId { get; set; }
    public int InvitationLifetimeDays { get; set; }
}

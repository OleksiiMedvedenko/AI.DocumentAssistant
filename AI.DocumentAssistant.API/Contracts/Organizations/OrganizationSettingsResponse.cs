namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class OrganizationSettingsResponse
{
    public Guid OrganizationId { get; set; }
    public int InvitationLifetimeDays { get; set; }
}

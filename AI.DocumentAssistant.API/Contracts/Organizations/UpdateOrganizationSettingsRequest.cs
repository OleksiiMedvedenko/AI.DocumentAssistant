namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class UpdateOrganizationSettingsRequest
{
    public int InvitationLifetimeDays { get; set; }
}

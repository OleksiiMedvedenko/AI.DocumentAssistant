namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class CreateOrganizationInvitationRequest
{
    public string Email { get; set; } = default!;
}

namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class AcceptOrganizationInvitationByCodeRequest
{
    public string Code { get; set; } = default!;
}

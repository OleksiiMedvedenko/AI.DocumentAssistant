namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class OrganizationInvitationAcceptResponse
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = default!;
    public DateTime JoinedAtUtc { get; set; }
}

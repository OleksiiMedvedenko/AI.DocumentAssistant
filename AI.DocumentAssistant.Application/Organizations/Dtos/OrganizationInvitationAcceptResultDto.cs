namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class OrganizationInvitationAcceptResultDto
{
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = default!;
    public DateTime JoinedAtUtc { get; set; }
}

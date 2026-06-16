namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class AddOrganizationMemberRequest
{
    public Guid? UserId { get; set; }
    public string? Email { get; set; }
}

namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class OrganizationInvitationResponse
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string OrganizationName { get; set; } = default!;
    public string Email { get; set; } = default!;
    public Guid InvitedUserId { get; set; }
    public string InvitedUserEmail { get; set; } = default!;
    public Guid InvitedByUserId { get; set; }
    public string InvitedByEmail { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
    public DateTime? AcceptedAtUtc { get; set; }
    public DateTime? RevokedAtUtc { get; set; }
}

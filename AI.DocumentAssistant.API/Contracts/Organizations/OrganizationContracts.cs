namespace AI.DocumentAssistant.API.Contracts.Organizations;

public sealed class CreateOrganizationRequest
{
    public string Name { get; set; } = default!;
}

public sealed class UpdateOrganizationSettingsRequest
{
    public bool? AllowPrivateDocuments { get; set; }
    public bool? AllowPublicShareLinks { get; set; }
}

public sealed class InviteOrganizationMemberRequest
{
    public string Email { get; set; } = default!;
    public List<string> Roles { get; set; } = new();
}

public sealed class AcceptOrganizationInvitationRequest
{
    public string Code { get; set; } = default!;
}

public sealed class UpdateOrganizationMemberRolesRequest
{
    public List<string> Roles { get; set; } = new();
}

public sealed class CreateTeamRequest
{
    public string Name { get; set; } = default!;
}

public sealed class AddTeamMemberRequest
{
    public Guid OrganizationMemberId { get; set; }
}

public sealed class GrantAccessRequest
{
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public string? Role { get; set; }
    public string ResourceType { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public string PermissionKey { get; set; } = default!;
}

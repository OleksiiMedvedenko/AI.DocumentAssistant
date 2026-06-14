namespace AI.DocumentAssistant.Application.Organizations.Dtos;

public sealed class CreateOrganizationRequestDto
{
    public string Name { get; set; } = default!;
}

public sealed class UpdateOrganizationSettingsRequestDto
{
    public bool? AllowPrivateDocuments { get; set; }
    public bool? AllowPublicShareLinks { get; set; }
}

public sealed class OrganizationDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public string Slug { get; set; } = default!;
    public Guid OwnerUserId { get; set; }
    public bool IsActive { get; set; }
    public bool AllowPrivateDocuments { get; set; }
    public bool AllowPublicShareLinks { get; set; }
    public DateTime CreatedAtUtc { get; set; }
    public List<string> CurrentUserRoles { get; set; } = new();
}

public sealed class OrganizationMemberDto
{
    public Guid Id { get; set; }
    public Guid UserId { get; set; }
    public string Email { get; set; } = default!;
    public string? DisplayName { get; set; }
    public string Status { get; set; } = default!;
    public DateTime JoinedAtUtc { get; set; }
    public List<string> Roles { get; set; } = new();
}

public sealed class InviteOrganizationMemberRequestDto
{
    public string Email { get; set; } = default!;
    public List<string> Roles { get; set; } = new();
}

public sealed class OrganizationInvitationDto
{
    public Guid Id { get; set; }
    public Guid OrganizationId { get; set; }
    public string Email { get; set; } = default!;
    public string Code { get; set; } = default!;
    public string Status { get; set; } = default!;
    public DateTime CreatedAtUtc { get; set; }
    public DateTime ExpiresAtUtc { get; set; }
}

public sealed class AcceptOrganizationInvitationRequestDto
{
    public string Code { get; set; } = default!;
}

public sealed class UpdateOrganizationMemberRolesRequestDto
{
    public List<string> Roles { get; set; } = new();
}

public sealed class RoleDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public bool IsSystemRole { get; set; }
    public List<string> Permissions { get; set; } = new();
}

public sealed class CreateTeamRequestDto
{
    public string Name { get; set; } = default!;
}

public sealed class TeamDto
{
    public Guid Id { get; set; }
    public string Name { get; set; } = default!;
    public int MemberCount { get; set; }
}

public sealed class AddTeamMemberRequestDto
{
    public Guid OrganizationMemberId { get; set; }
}

public sealed class GrantAccessRequestDto
{
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public string? Role { get; set; }
    public string ResourceType { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public string PermissionKey { get; set; } = default!;
}

public sealed class AccessGrantDto
{
    public Guid Id { get; set; }
    public Guid? UserId { get; set; }
    public Guid? TeamId { get; set; }
    public string? Role { get; set; }
    public string ResourceType { get; set; } = default!;
    public Guid ResourceId { get; set; }
    public string PermissionKey { get; set; } = default!;
    public DateTime GrantedAtUtc { get; set; }
}

public sealed class AuditLogDto
{
    public Guid Id { get; set; }
    public Guid? ActorUserId { get; set; }
    public string Action { get; set; } = default!;
    public string? ResourceType { get; set; }
    public Guid? ResourceId { get; set; }
    public string? DetailsJson { get; set; }
    public DateTime OccurredAtUtc { get; set; }
}

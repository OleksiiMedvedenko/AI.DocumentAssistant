using AI.DocumentAssistant.Application.Organizations.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto> CreateAsync(CreateOrganizationRequestDto request, CancellationToken cancellationToken);
    Task<List<OrganizationDto>> GetMineAsync(CancellationToken cancellationToken);
    Task<OrganizationDto> GetByIdAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<OrganizationDto> UpdateSettingsAsync(Guid organizationId, UpdateOrganizationSettingsRequestDto request, CancellationToken cancellationToken);
    Task<List<OrganizationMemberDto>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<OrganizationInvitationDto> InviteMemberAsync(Guid organizationId, InviteOrganizationMemberRequestDto request, CancellationToken cancellationToken);
    Task<OrganizationDto> AcceptInvitationAsync(AcceptOrganizationInvitationRequestDto request, CancellationToken cancellationToken);
    Task<OrganizationMemberDto> UpdateMemberRolesAsync(Guid organizationId, Guid memberId, UpdateOrganizationMemberRolesRequestDto request, CancellationToken cancellationToken);
    Task RemoveMemberAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken);
    Task<List<RoleDto>> GetRolesAsync(Guid organizationId, CancellationToken cancellationToken);
    Task<TeamDto> CreateTeamAsync(Guid organizationId, CreateTeamRequestDto request, CancellationToken cancellationToken);
    Task<List<TeamDto>> GetTeamsAsync(Guid organizationId, CancellationToken cancellationToken);
    Task AddTeamMemberAsync(Guid organizationId, Guid teamId, AddTeamMemberRequestDto request, CancellationToken cancellationToken);
    Task RemoveTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, CancellationToken cancellationToken);
    Task<AccessGrantDto> GrantAccessAsync(Guid organizationId, GrantAccessRequestDto request, CancellationToken cancellationToken);
    Task<List<AccessGrantDto>> GetAccessGrantsAsync(Guid organizationId, string resourceType, Guid resourceId, CancellationToken cancellationToken);
    Task RevokeAccessAsync(Guid organizationId, Guid grantId, CancellationToken cancellationToken);
    Task<List<AuditLogDto>> GetAuditLogsAsync(Guid organizationId, CancellationToken cancellationToken);
}

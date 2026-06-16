using AI.DocumentAssistant.Application.Organizations.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Organizations;

public interface IOrganizationInvitationService
{
    Task<OrganizationInvitationDto> CreateInvitationAsync(Guid organizationId, CreateOrganizationInvitationDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationInvitationDto>> GetOrganizationInvitationsAsync(Guid organizationId, bool includeHistory = false, CancellationToken cancellationToken = default);
    Task RevokeInvitationAsync(Guid organizationId, Guid invitationId, CancellationToken cancellationToken = default);
    Task<OrganizationInvitationAcceptResultDto> AcceptInvitationAsync(Guid invitationId, CancellationToken cancellationToken = default);
    Task<OrganizationInvitationAcceptResultDto> AcceptInvitationByCodeAsync(string code, CancellationToken cancellationToken = default);
    Task<OrganizationSettingsDto> GetSettingsAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationSettingsDto> UpdateSettingsAsync(Guid organizationId, UpdateOrganizationSettingsDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationActivityLogDto>> GetActivityLogsAsync(Guid organizationId, CancellationToken cancellationToken = default);
}

using AI.DocumentAssistant.Application.Organizations.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Organizations;

public interface IOrganizationService
{
    Task<OrganizationDto> CreateAsync(CreateOrganizationDto dto, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationDto>> GetMyOrganizationsAsync(CancellationToken cancellationToken = default);
    Task<OrganizationDto> GetByIdAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationDto> UpdateAsync(Guid organizationId, UpdateOrganizationDto dto, CancellationToken cancellationToken = default);
    Task DeleteAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationDto> ReactivateAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<OrganizationMemberDto>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken = default);
    Task<OrganizationMemberDto> AddMemberAsync(Guid organizationId, AddOrganizationMemberDto dto, CancellationToken cancellationToken = default);
    Task RemoveMemberAsync(Guid organizationId, Guid userId, CancellationToken cancellationToken = default);
}

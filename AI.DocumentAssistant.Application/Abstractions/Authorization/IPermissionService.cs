using AI.DocumentAssistant.Domain.Enums;

namespace AI.DocumentAssistant.Application.Abstractions.Authorization;

public interface IPermissionService
{
    Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionKey,
        AccessResourceType? resourceType = null,
        Guid? resourceId = null,
        Guid? organizationId = null,
        CancellationToken cancellationToken = default);

    Task EnsurePermissionAsync(
        Guid userId,
        string permissionKey,
        AccessResourceType? resourceType = null,
        Guid? resourceId = null,
        Guid? organizationId = null,
        CancellationToken cancellationToken = default);
}

using AI.DocumentAssistant.Application.Abstractions.Authorization;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Authorization;

public sealed class PermissionService : IPermissionService
{
    private readonly AppDbContext _dbContext;

    public PermissionService(AppDbContext dbContext)
    {
        _dbContext = dbContext;
    }

    public async Task<bool> HasPermissionAsync(
        Guid userId,
        string permissionKey,
        AccessResourceType? resourceType = null,
        Guid? resourceId = null,
        Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(permissionKey))
        {
            return false;
        }

        var user = await _dbContext.Users
            .AsNoTracking()
            .FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken);

        if (user is null)
        {
            return false;
        }

        if (user.Role == UserRole.Admin && permissionKey.StartsWith("admin.", StringComparison.OrdinalIgnoreCase))
        {
            return true;
        }

        if (resourceType == AccessResourceType.Document && resourceId.HasValue)
        {
            var document = await _dbContext.Documents
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resourceId.Value, cancellationToken);

            if (document is null)
            {
                return false;
            }

            if (document.UserId == userId)
            {
                return true;
            }

            organizationId = document.OrganizationId;
            if (organizationId is null || document.Visibility == DocumentVisibility.Private)
            {
                return false;
            }

            if (document.Visibility == DocumentVisibility.Organization &&
                await HasOrganizationRolePermissionAsync(userId, organizationId.Value, permissionKey, cancellationToken))
            {
                return true;
            }

            return await HasExplicitGrantAsync(
                userId,
                organizationId.Value,
                AccessResourceType.Document,
                document.Id,
                permissionKey,
                cancellationToken);
        }

        if (resourceType == AccessResourceType.Folder && resourceId.HasValue)
        {
            var folder = await _dbContext.DocumentFolders
                .AsNoTracking()
                .FirstOrDefaultAsync(x => x.Id == resourceId.Value, cancellationToken);

            if (folder is null)
            {
                return false;
            }

            if (folder.UserId == userId)
            {
                return true;
            }

            organizationId = folder.OrganizationId;
            if (organizationId is null || folder.Visibility == DocumentVisibility.Private)
            {
                return false;
            }

            if (folder.Visibility == DocumentVisibility.Organization &&
                await HasOrganizationRolePermissionAsync(userId, organizationId.Value, permissionKey, cancellationToken))
            {
                return true;
            }

            return await HasExplicitGrantAsync(
                userId,
                organizationId.Value,
                AccessResourceType.Folder,
                folder.Id,
                permissionKey,
                cancellationToken);
        }

        if (organizationId.HasValue)
        {
            return await HasOrganizationRolePermissionAsync(userId, organizationId.Value, permissionKey, cancellationToken);
        }

        return user.Role == UserRole.Admin;
    }

    public async Task EnsurePermissionAsync(
        Guid userId,
        string permissionKey,
        AccessResourceType? resourceType = null,
        Guid? resourceId = null,
        Guid? organizationId = null,
        CancellationToken cancellationToken = default)
    {
        var allowed = await HasPermissionAsync(
            userId,
            permissionKey,
            resourceType,
            resourceId,
            organizationId,
            cancellationToken);

        if (!allowed)
        {
            throw new ForbiddenException("You do not have permission to perform this action.");
        }
    }

    private async Task<bool> HasOrganizationRolePermissionAsync(
        Guid userId,
        Guid organizationId,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.Roles)
                .ThenInclude(x => x.Role)
                    .ThenInclude(x => x.Permissions)
                        .ThenInclude(x => x.Permission)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.UserId == userId &&
                x.Status == OrganizationMemberStatus.Active,
                cancellationToken);

        if (membership is null)
        {
            return false;
        }

        var isOwner = await _dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(x => x.Id == organizationId && x.OwnerUserId == userId && x.IsActive, cancellationToken);

        if (isOwner)
        {
            return true;
        }

        return membership.Roles
            .SelectMany(x => x.Role.Permissions)
            .Any(x => x.Permission.Key == permissionKey);
    }

    private async Task<bool> HasExplicitGrantAsync(
        Guid userId,
        Guid organizationId,
        AccessResourceType resourceType,
        Guid resourceId,
        string permissionKey,
        CancellationToken cancellationToken)
    {
        var membership = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.TeamMemberships)
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x =>
                x.OrganizationId == organizationId &&
                x.UserId == userId &&
                x.Status == OrganizationMemberStatus.Active,
                cancellationToken);

        if (membership is null)
        {
            return false;
        }

        var roleIds = membership.Roles.Select(x => x.RoleId).ToArray();
        var teamIds = membership.TeamMemberships.Select(x => x.TeamId).ToArray();

        return await _dbContext.AccessGrants
            .AsNoTracking()
            .AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.ResourceType == resourceType &&
                x.ResourceId == resourceId &&
                x.PermissionKey == permissionKey &&
                (
                    x.UserId == userId ||
                    (x.TeamId != null && teamIds.Contains(x.TeamId.Value)) ||
                    (x.RoleId != null && roleIds.Contains(x.RoleId.Value))
                ),
                cancellationToken);
    }
}

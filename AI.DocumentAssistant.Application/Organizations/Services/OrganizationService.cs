using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Organizations;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;
using System.Text.Json;

namespace AI.DocumentAssistant.Application.Organizations.Services;

public sealed class OrganizationService : IOrganizationService
{
    private const int OrganizationNameMaxLength = 200;

    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public OrganizationService(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<OrganizationDto> CreateAsync(
        CreateOrganizationDto dto,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        EnsureCanCreateOrganization(currentUser);

        var name = NormalizeOrganizationName(dto.Name);
        await EnsureActiveOrganizationNameIsUniqueAsync(name, null, cancellationToken);

        var now = DateTime.UtcNow;

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            CreatedByUserId = currentUser.Id,
            CreatedAtUtc = now,
            IsActive = true
        };

        var ownerMembership = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            UserId = currentUser.Id,
            AddedByUserId = currentUser.Id,
            JoinedAtUtc = now,
            IsActive = true
        };

        _dbContext.Organizations.Add(organization);
        _dbContext.OrganizationMembers.Add(ownerMembership);
        _dbContext.OrganizationSettings.Add(new OrganizationSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            InvitationLifetimeDays = 3,
            CreatedAtUtc = now
        });
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationCreated, new
        {
            organization.Name
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            CreatedByUserId = organization.CreatedByUserId,
            CreatedByEmail = currentUser.Email,
            CreatedAtUtc = organization.CreatedAtUtc,
            UpdatedAtUtc = organization.UpdatedAtUtc,
            IsActive = organization.IsActive,
            ActiveMembersCount = 1,
            CanManage = true
        };
    }

    public async Task<IReadOnlyList<OrganizationDto>> GetMyOrganizationsAsync(
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);

        IQueryable<Organization> query = _dbContext.Organizations
            .AsNoTracking()
            .Include(x => x.CreatedByUser)
            .Include(x => x.Members);

        if (currentUser.Role == UserRole.Admin)
        {
            // Application admins can audit and restore every organization, including inactive ones.
        }
        else if (currentUser.Role == UserRole.Manager)
        {
            query = query.Where(x =>
                x.CreatedByUserId == currentUser.Id ||
                x.Members.Any(m => m.UserId == currentUser.Id && m.IsActive));
        }
        else
        {
            query = query.Where(x =>
                x.IsActive &&
                x.Members.Any(m => m.UserId == currentUser.Id && m.IsActive));
        }

        var organizations = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return organizations
            .Select(x => MapOrganization(x, currentUser))
            .ToList();
    }

    public async Task<OrganizationDto> GetByIdAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);

        return MapOrganization(organization, currentUser);
    }

    public async Task<OrganizationDto> UpdateAsync(
        Guid organizationId,
        UpdateOrganizationDto dto,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        var normalizedName = NormalizeOrganizationName(dto.Name);
        await EnsureActiveOrganizationNameIsUniqueAsync(normalizedName, organizationId, cancellationToken);

        organization.Name = normalizedName;
        organization.UpdatedAtUtc = DateTime.UtcNow;
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationUpdated, new
        {
            organization.Name
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapOrganization(organization, currentUser);
    }

    public async Task DeleteAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        organization.IsActive = false;
        organization.UpdatedAtUtc = DateTime.UtcNow;
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationDeactivated, new
        {
            organization.Name
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrganizationDto> ReactivateAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        organization.IsActive = true;
        organization.UpdatedAtUtc = DateTime.UtcNow;
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationReactivated, new
        {
            organization.Name
        });

        await EnsureActiveOrganizationNameIsUniqueAsync(organization.Name, organization.Id, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return MapOrganization(organization, currentUser);
    }

    public async Task<IReadOnlyList<OrganizationMemberDto>> GetMembersAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);

        return await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Where(x => x.OrganizationId == organizationId && x.IsActive)
            .OrderBy(x => x.JoinedAtUtc)
            .Select(x => new OrganizationMemberDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                UserId = x.UserId,
                Email = x.User.Email,
                DisplayName = x.User.DisplayName,
                UserRole = x.User.Role.ToString(),
                AddedByUserId = x.AddedByUserId,
                JoinedAtUtc = x.JoinedAtUtc,
                RemovedAtUtc = x.RemovedAtUtc,
                IsActive = x.IsActive
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<OrganizationMemberDto> AddMemberAsync(
        Guid organizationId,
        AddOrganizationMemberDto dto,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        var userToAdd = await FindUserToAddAsync(dto, cancellationToken);
        if (!userToAdd.IsActive)
        {
            throw new BadRequestException("ORGANIZATION_CANNOT_ADD_INACTIVE_USER");
        }

        var now = DateTime.UtcNow;
        var existingMembership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userToAdd.Id, cancellationToken);

        if (existingMembership is not null)
        {
            if (existingMembership.IsActive)
            {
                throw new ConflictException("ORGANIZATION_USER_ALREADY_ACTIVE_MEMBER");
            }

            existingMembership.IsActive = true;
            existingMembership.RemovedAtUtc = null;
            existingMembership.JoinedAtUtc = now;
            existingMembership.AddedByUserId = currentUser.Id;
        }
        else
        {
            existingMembership = new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                UserId = userToAdd.Id,
                AddedByUserId = currentUser.Id,
                JoinedAtUtc = now,
                IsActive = true
            };

            _dbContext.OrganizationMembers.Add(existingMembership);
        }

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new OrganizationMemberDto
        {
            Id = existingMembership.Id,
            OrganizationId = existingMembership.OrganizationId,
            UserId = userToAdd.Id,
            Email = userToAdd.Email,
            DisplayName = userToAdd.DisplayName,
            UserRole = userToAdd.Role.ToString(),
            AddedByUserId = existingMembership.AddedByUserId,
            JoinedAtUtc = existingMembership.JoinedAtUtc,
            RemovedAtUtc = existingMembership.RemovedAtUtc,
            IsActive = existingMembership.IsActive
        };
    }

    public async Task RemoveMemberAsync(
        Guid organizationId,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        if (userId == currentUser.Id)
        {
            throw new BadRequestException("ORGANIZATION_CANNOT_REMOVE_YOURSELF");
        }

        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId, cancellationToken);

        if (membership is null || !membership.IsActive)
        {
            throw new NotFoundException("ORGANIZATION_MEMBER_NOT_FOUND");
        }

        var activeMembersCount = await _dbContext.OrganizationMembers
            .CountAsync(x => x.OrganizationId == organizationId && x.IsActive, cancellationToken);

        if (activeMembersCount <= 1)
        {
            throw new BadRequestException("ORGANIZATION_CANNOT_REMOVE_LAST_ACTIVE_MEMBER");
        }

        membership.IsActive = false;
        membership.RemovedAtUtc = DateTime.UtcNow;
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationMemberRemoved, new
        {
            removedUserId = userId
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<User> GetCurrentActiveUserAsync(CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetUserId();
        var currentUser = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Id == currentUserId, cancellationToken);

        if (currentUser is null || !currentUser.IsActive)
        {
            throw new UnauthorizedException("AUTH_USER_NOT_AUTHENTICATED");
        }

        return currentUser;
    }

    private static void EnsureCanCreateOrganization(User currentUser)
    {
        if (currentUser.Role is not (UserRole.Admin or UserRole.Manager))
        {
            throw new ForbiddenException("ORGANIZATION_CREATE_REQUIRES_ADMIN_OR_MANAGER");
        }
    }

    private static void EnsureCanManageOrganization(User currentUser, Organization organization)
    {
        if (currentUser.Role == UserRole.Admin)
        {
            return;
        }

        var isActiveMember = organization.Members.Any(x => x.UserId == currentUser.Id && x.IsActive);
        if (currentUser.Role == UserRole.Manager &&
            (organization.CreatedByUserId == currentUser.Id || isActiveMember))
        {
            return;
        }

        throw new ForbiddenException("ORGANIZATION_MANAGE_FORBIDDEN");
    }

    private async Task<Organization> GetVisibleOrganizationAsync(
        Guid organizationId,
        User currentUser,
        CancellationToken cancellationToken)
    {
        var organization = await _dbContext.Organizations
            .Include(x => x.CreatedByUser)
            .Include(x => x.Members)
            .FirstOrDefaultAsync(x => x.Id == organizationId, cancellationToken);

        if (organization is null)
        {
            throw new NotFoundException("ORGANIZATION_NOT_FOUND");
        }

        if (currentUser.Role == UserRole.Admin)
        {
            return organization;
        }

        var isActiveMember = organization.Members.Any(x => x.UserId == currentUser.Id && x.IsActive);
        if (currentUser.Role == UserRole.Manager &&
            (organization.CreatedByUserId == currentUser.Id || isActiveMember))
        {
            return organization;
        }

        if (organization.IsActive && isActiveMember)
        {
            return organization;
        }

        throw new NotFoundException("ORGANIZATION_NOT_FOUND");
    }

    private async Task<User> FindUserToAddAsync(
        AddOrganizationMemberDto dto,
        CancellationToken cancellationToken)
    {
        if (dto.UserId is null && string.IsNullOrWhiteSpace(dto.Email))
        {
            throw new BadRequestException("ORGANIZATION_MEMBER_USER_ID_OR_EMAIL_REQUIRED");
        }

        if (dto.UserId is not null)
        {
            var userById = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == dto.UserId, cancellationToken);
            if (userById is null)
            {
                throw new NotFoundException("USER_NOT_FOUND");
            }

            return userById;
        }

        var normalizedEmail = dto.Email!.Trim().ToLowerInvariant();
        var userByEmail = await _dbContext.Users.FirstOrDefaultAsync(x => x.Email == normalizedEmail, cancellationToken);
        if (userByEmail is null)
        {
            throw new NotFoundException("USER_NOT_FOUND");
        }

        return userByEmail;
    }

    private async Task EnsureActiveOrganizationNameIsUniqueAsync(
        string normalizedName,
        Guid? excludedOrganizationId,
        CancellationToken cancellationToken)
    {
        var normalizedLowerName = normalizedName.ToLower();
        var duplicateExists = await _dbContext.Organizations
            .AsNoTracking()
            .AnyAsync(x => x.IsActive &&
                           x.Name.ToLower() == normalizedLowerName &&
                           (excludedOrganizationId == null || x.Id != excludedOrganizationId.Value),
                cancellationToken);

        if (duplicateExists)
        {
            throw new ConflictException("ORGANIZATION_ACTIVE_NAME_ALREADY_EXISTS");
        }
    }

    private void AddActivityLog(Guid organizationId, Guid? actorUserId, OrganizationActivityActionType actionType, object payload)
    {
        _dbContext.OrganizationActivityLogs.Add(new OrganizationActivityLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            ActionType = actionType,
            PayloadJson = JsonSerializer.Serialize(payload),
            CreatedAtUtc = DateTime.UtcNow
        });
    }

    private static string NormalizeOrganizationName(string? name)
    {
        var normalizedName = name?.Trim();
        if (string.IsNullOrWhiteSpace(normalizedName))
        {
            throw new BadRequestException("ORGANIZATION_NAME_REQUIRED");
        }

        if (normalizedName.Length > OrganizationNameMaxLength)
        {
            throw new BadRequestException("ORGANIZATION_NAME_TOO_LONG");
        }

        return normalizedName;
    }

    private static OrganizationDto MapOrganization(Organization organization, User currentUser)
    {
        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            CreatedByUserId = organization.CreatedByUserId,
            CreatedByEmail = organization.CreatedByUser.Email,
            CreatedAtUtc = organization.CreatedAtUtc,
            UpdatedAtUtc = organization.UpdatedAtUtc,
            IsActive = organization.IsActive,
            ActiveMembersCount = organization.Members.Count(x => x.IsActive),
            CanManage = currentUser.Role == UserRole.Admin ||
                        (currentUser.Role == UserRole.Manager &&
                         (organization.CreatedByUserId == currentUser.Id ||
                          organization.Members.Any(x => x.UserId == currentUser.Id && x.IsActive)))
        };
    }
}

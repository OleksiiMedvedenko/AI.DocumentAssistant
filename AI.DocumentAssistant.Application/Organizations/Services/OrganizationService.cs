using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using AI.DocumentAssistant.Application.Abstractions.Authorization;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Organizations;
using AI.DocumentAssistant.Application.Authorization;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Organizations.Services;

public sealed class OrganizationService : IOrganizationService
{
    private readonly AppDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IPermissionService _permissionService;

    public OrganizationService(
        AppDbContext dbContext,
        ICurrentUserService currentUserService,
        IPermissionService permissionService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _permissionService = permissionService;
    }

    public async Task<OrganizationDto> CreateAsync(CreateOrganizationRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var name = NormalizeRequired(request.Name, "Organization name");
        var slug = await CreateUniqueSlugAsync(name, cancellationToken);

        await EnsurePermissionCatalogAsync(cancellationToken);

        var organization = new Organization
        {
            Id = Guid.NewGuid(),
            Name = name,
            Slug = slug,
            OwnerUserId = userId,
            CreatedAtUtc = DateTime.UtcNow
        };

        var member = new OrganizationMember
        {
            Id = Guid.NewGuid(),
            OrganizationId = organization.Id,
            UserId = userId,
            Status = OrganizationMemberStatus.Active,
            JoinedAtUtc = DateTime.UtcNow
        };

        var roles = await CreateDefaultRolesAsync(organization.Id, cancellationToken);
        var ownerRole = roles.Single(x => x.NormalizedName == NormalizeName(OrganizationRoleDefaults.Owner));
        member.Roles.Add(new OrganizationMemberRole
        {
            OrganizationMemberId = member.Id,
            RoleId = ownerRole.Id
        });

        _dbContext.Organizations.Add(organization);
        _dbContext.OrganizationMembers.Add(member);
        await AddAuditAsync(organization.Id, userId, AuditLogAction.OrganizationCreated, AccessResourceType.Organization, organization.Id, new { organization.Name }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(organization.Id, cancellationToken);
    }

    public async Task<List<OrganizationDto>> GetMineAsync(CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();

        var organizations = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .Where(x => x.UserId == userId && x.Status == OrganizationMemberStatus.Active && x.Organization.IsActive)
            .OrderBy(x => x.Organization.Name)
            .ToListAsync(cancellationToken);

        return organizations.Select(x => ToOrganizationDto(x.Organization, x.Roles.Select(r => r.Role.Name))).ToList();
    }

    public async Task<OrganizationDto> GetByIdAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationViewMembers, organizationId: organizationId, cancellationToken: cancellationToken);

        var membership = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.UserId == userId && x.Status == OrganizationMemberStatus.Active, cancellationToken);

        if (membership is null)
        {
            throw new NotFoundException("Organization not found.");
        }

        return ToOrganizationDto(membership.Organization, membership.Roles.Select(x => x.Role.Name));
    }

    public async Task<OrganizationDto> UpdateSettingsAsync(Guid organizationId, UpdateOrganizationSettingsRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageSettings, organizationId: organizationId, cancellationToken: cancellationToken);

        var organization = await _dbContext.Organizations.FirstOrDefaultAsync(x => x.Id == organizationId && x.IsActive, cancellationToken)
            ?? throw new NotFoundException("Organization not found.");

        if (request.AllowPrivateDocuments.HasValue)
        {
            organization.AllowPrivateDocuments = request.AllowPrivateDocuments.Value;
        }

        if (request.AllowPublicShareLinks.HasValue)
        {
            organization.AllowPublicShareLinks = request.AllowPublicShareLinks.Value;
        }

        organization.UpdatedAtUtc = DateTime.UtcNow;
        await AddAuditAsync(organization.Id, userId, AuditLogAction.MemberRoleChanged, AccessResourceType.Organization, organization.Id, request, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(organization.Id, cancellationToken);
    }

    public async Task<List<OrganizationMemberDto>> GetMembersAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationViewMembers, organizationId: organizationId, cancellationToken: cancellationToken);

        var members = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .Where(x => x.OrganizationId == organizationId && x.Status != OrganizationMemberStatus.Removed)
            .OrderBy(x => x.User.Email)
            .ToListAsync(cancellationToken);

        return members.Select(ToMemberDto).ToList();
    }

    public async Task<OrganizationInvitationDto> InviteMemberAsync(Guid organizationId, InviteOrganizationMemberRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationInviteMembers, organizationId: organizationId, cancellationToken: cancellationToken);

        var email = NormalizeEmail(request.Email);
        var organizationExists = await _dbContext.Organizations.AnyAsync(x => x.Id == organizationId && x.IsActive, cancellationToken);
        if (!organizationExists)
        {
            throw new NotFoundException("Organization not found.");
        }

        var existingUserId = await _dbContext.Users
            .Where(x => x.Email == email)
            .Select(x => (Guid?)x.Id)
            .FirstOrDefaultAsync(cancellationToken);

        if (existingUserId.HasValue)
        {
            var alreadyMember = await _dbContext.OrganizationMembers.AnyAsync(x =>
                x.OrganizationId == organizationId && x.UserId == existingUserId.Value && x.Status == OrganizationMemberStatus.Active,
                cancellationToken);

            if (alreadyMember)
            {
                throw new ConflictException("This user is already an active member of the organization.");
            }
        }

        var pending = await _dbContext.OrganizationInvitations.FirstOrDefaultAsync(x =>
            x.OrganizationId == organizationId && x.Email == email && x.Status == OrganizationInvitationStatus.Pending,
            cancellationToken);

        if (pending is not null)
        {
            return ToInvitationDto(pending);
        }

        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            Code = await CreateUniqueInviteCodeAsync(cancellationToken),
            TokenHash = HashToken(Convert.ToBase64String(RandomNumberGenerator.GetBytes(32))),
            Status = OrganizationInvitationStatus.Pending,
            InvitedByUserId = userId,
            CreatedAtUtc = DateTime.UtcNow,
            ExpiresAtUtc = DateTime.UtcNow.AddDays(14)
        };

        _dbContext.OrganizationInvitations.Add(invitation);
        await AddAuditAsync(organizationId, userId, AuditLogAction.MemberInvited, AccessResourceType.Organization, organizationId, new { invitation.Email }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToInvitationDto(invitation);
    }

    public async Task<OrganizationDto> AcceptInvitationAsync(AcceptOrganizationInvitationRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        var code = NormalizeRequired(request.Code, "Invitation code").Trim().ToUpperInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == userId && x.IsActive, cancellationToken)
            ?? throw new UnauthorizedException("User is not authenticated.");

        var invitation = await _dbContext.OrganizationInvitations
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Code == code, cancellationToken)
            ?? throw new NotFoundException("Invitation not found.");

        if (invitation.Status != OrganizationInvitationStatus.Pending || invitation.ExpiresAtUtc < DateTime.UtcNow)
        {
            throw new BadRequestException("Invitation is not active.");
        }

        if (!string.Equals(invitation.Email, user.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("Invitation was sent to another email address.");
        }

        var member = await _dbContext.OrganizationMembers
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.OrganizationId == invitation.OrganizationId && x.UserId == userId, cancellationToken);

        if (member is null)
        {
            member = new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = invitation.OrganizationId,
                UserId = userId,
                Status = OrganizationMemberStatus.Active,
                JoinedAtUtc = DateTime.UtcNow
            };
            _dbContext.OrganizationMembers.Add(member);
        }
        else
        {
            member.Status = OrganizationMemberStatus.Active;
            member.JoinedAtUtc = DateTime.UtcNow;
        }

        var memberRole = await GetRoleByNameAsync(invitation.OrganizationId, OrganizationRoleDefaults.Member, cancellationToken);
        if (!member.Roles.Any(x => x.RoleId == memberRole.Id))
        {
            member.Roles.Add(new OrganizationMemberRole { OrganizationMemberId = member.Id, RoleId = memberRole.Id });
        }

        invitation.Status = OrganizationInvitationStatus.Accepted;
        invitation.AcceptedAtUtc = DateTime.UtcNow;
        invitation.AcceptedByUserId = userId;
        await AddAuditAsync(invitation.OrganizationId, userId, AuditLogAction.InvitationAccepted, AccessResourceType.Organization, invitation.OrganizationId, new { invitation.Email }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        return await GetByIdAsync(invitation.OrganizationId, cancellationToken);
    }

    public async Task<OrganizationMemberDto> UpdateMemberRolesAsync(Guid organizationId, Guid memberId, UpdateOrganizationMemberRolesRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageRoles, organizationId: organizationId, cancellationToken: cancellationToken);

        var member = await _dbContext.OrganizationMembers
            .Include(x => x.User)
            .Include(x => x.Roles)
            .FirstOrDefaultAsync(x => x.Id == memberId && x.OrganizationId == organizationId && x.Status == OrganizationMemberStatus.Active, cancellationToken)
            ?? throw new NotFoundException("Member not found.");

        var organization = await _dbContext.Organizations.FirstAsync(x => x.Id == organizationId, cancellationToken);
        if (member.UserId == organization.OwnerUserId && !request.Roles.Any(x => NormalizeName(x) == NormalizeName(OrganizationRoleDefaults.Owner)))
        {
            throw new BadRequestException("The organization owner must keep the Owner role.");
        }

        var roleNames = request.Roles.Count == 0 ? new List<string> { OrganizationRoleDefaults.Member } : request.Roles;
        var roles = await _dbContext.Roles
            .Where(x => x.OrganizationId == organizationId && roleNames.Select(NormalizeName).Contains(x.NormalizedName))
            .ToListAsync(cancellationToken);

        if (roles.Count != roleNames.Select(NormalizeName).Distinct().Count())
        {
            throw new BadRequestException("One or more roles do not exist in this organization.");
        }

        member.Roles.Clear();
        foreach (var role in roles)
        {
            member.Roles.Add(new OrganizationMemberRole { OrganizationMemberId = member.Id, RoleId = role.Id });
        }

        await AddAuditAsync(organizationId, userId, AuditLogAction.MemberRoleChanged, AccessResourceType.Organization, organizationId, new { memberId, roles = roles.Select(x => x.Name) }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);

        var updatedMember = await _dbContext.OrganizationMembers
            .AsNoTracking()
            .Include(x => x.User)
            .Include(x => x.Roles).ThenInclude(x => x.Role)
            .FirstAsync(x => x.Id == member.Id, cancellationToken);

        return ToMemberDto(updatedMember);
    }

    public async Task RemoveMemberAsync(Guid organizationId, Guid memberId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationRemoveMembers, organizationId: organizationId, cancellationToken: cancellationToken);

        var member = await _dbContext.OrganizationMembers.FirstOrDefaultAsync(x => x.Id == memberId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Member not found.");
        var organization = await _dbContext.Organizations.FirstAsync(x => x.Id == organizationId, cancellationToken);
        if (member.UserId == organization.OwnerUserId)
        {
            throw new BadRequestException("The organization owner cannot be removed.");
        }

        member.Status = OrganizationMemberStatus.Removed;
        await AddAuditAsync(organizationId, userId, AuditLogAction.MemberRemoved, AccessResourceType.Organization, organizationId, new { memberId }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<RoleDto>> GetRolesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageRoles, organizationId: organizationId, cancellationToken: cancellationToken);

        var roles = await _dbContext.Roles
            .AsNoTracking()
            .Include(x => x.Permissions).ThenInclude(x => x.Permission)
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name)
            .ToListAsync(cancellationToken);

        return roles.Select(ToRoleDto).ToList();
    }

    public async Task<TeamDto> CreateTeamAsync(Guid organizationId, CreateTeamRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageRoles, organizationId: organizationId, cancellationToken: cancellationToken);
        var name = NormalizeRequired(request.Name, "Team name");
        var normalized = NormalizeName(name);

        var exists = await _dbContext.Teams.AnyAsync(x => x.OrganizationId == organizationId && x.NormalizedName == normalized, cancellationToken);
        if (exists)
        {
            throw new ConflictException("A team with this name already exists.");
        }

        var team = new Team
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Name = name,
            NormalizedName = normalized,
            CreatedAtUtc = DateTime.UtcNow
        };

        _dbContext.Teams.Add(team);
        await AddAuditAsync(organizationId, userId, AuditLogAction.TeamCreated, AccessResourceType.Organization, organizationId, new { team.Name }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return new TeamDto { Id = team.Id, Name = team.Name, MemberCount = 0 };
    }

    public async Task<List<TeamDto>> GetTeamsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationViewMembers, organizationId: organizationId, cancellationToken: cancellationToken);

        return await _dbContext.Teams
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderBy(x => x.Name)
            .Select(x => new TeamDto { Id = x.Id, Name = x.Name, MemberCount = x.Members.Count })
            .ToListAsync(cancellationToken);
    }

    public async Task AddTeamMemberAsync(Guid organizationId, Guid teamId, AddTeamMemberRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageRoles, organizationId: organizationId, cancellationToken: cancellationToken);

        var teamExists = await _dbContext.Teams.AnyAsync(x => x.Id == teamId && x.OrganizationId == organizationId, cancellationToken);
        var memberExists = await _dbContext.OrganizationMembers.AnyAsync(x => x.Id == request.OrganizationMemberId && x.OrganizationId == organizationId && x.Status == OrganizationMemberStatus.Active, cancellationToken);
        if (!teamExists || !memberExists)
        {
            throw new NotFoundException("Team or member not found.");
        }

        var exists = await _dbContext.TeamMembers.AnyAsync(x => x.TeamId == teamId && x.OrganizationMemberId == request.OrganizationMemberId, cancellationToken);
        if (!exists)
        {
            _dbContext.TeamMembers.Add(new TeamMember { TeamId = teamId, OrganizationMemberId = request.OrganizationMemberId, AddedAtUtc = DateTime.UtcNow });
            await AddAuditAsync(organizationId, userId, AuditLogAction.TeamMemberAdded, AccessResourceType.Organization, organizationId, new { teamId, request.OrganizationMemberId }, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
        }
    }

    public async Task RemoveTeamMemberAsync(Guid organizationId, Guid teamId, Guid memberId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.OrganizationManageRoles, organizationId: organizationId, cancellationToken: cancellationToken);

        var teamMember = await _dbContext.TeamMembers
            .Include(x => x.Team)
            .FirstOrDefaultAsync(x => x.TeamId == teamId && x.OrganizationMemberId == memberId && x.Team.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Team member not found.");

        _dbContext.TeamMembers.Remove(teamMember);
        await AddAuditAsync(organizationId, userId, AuditLogAction.TeamMemberRemoved, AccessResourceType.Organization, organizationId, new { teamId, memberId }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<AccessGrantDto> GrantAccessAsync(Guid organizationId, GrantAccessRequestDto request, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.FoldersManageAccess, organizationId: organizationId, cancellationToken: cancellationToken);

        if (!Enum.TryParse<AccessResourceType>(request.ResourceType, true, out var resourceType))
        {
            throw new BadRequestException("Unsupported resource type.");
        }

        if (!PermissionKeys.All.ContainsKey(request.PermissionKey))
        {
            throw new BadRequestException("Unsupported permission key.");
        }

        var targetCount = (request.UserId.HasValue ? 1 : 0) + (request.TeamId.HasValue ? 1 : 0) + (!string.IsNullOrWhiteSpace(request.Role) ? 1 : 0);
        if (targetCount != 1)
        {
            throw new BadRequestException("Exactly one grant target must be provided: userId, teamId or role.");
        }

        await EnsureResourceBelongsToOrganizationAsync(organizationId, resourceType, request.ResourceId, cancellationToken);

        if (request.UserId.HasValue)
        {
            var userIsMember = await _dbContext.OrganizationMembers.AnyAsync(x =>
                x.OrganizationId == organizationId &&
                x.UserId == request.UserId.Value &&
                x.Status == OrganizationMemberStatus.Active,
                cancellationToken);
            if (!userIsMember)
            {
                throw new BadRequestException("Access can only be granted to an active organization member.");
            }
        }

        if (request.TeamId.HasValue)
        {
            var teamExists = await _dbContext.Teams.AnyAsync(x => x.Id == request.TeamId.Value && x.OrganizationId == organizationId, cancellationToken);
            if (!teamExists)
            {
                throw new BadRequestException("Team does not belong to this organization.");
            }
        }

        Guid? roleId = null;
        if (!string.IsNullOrWhiteSpace(request.Role))
        {
            roleId = (await GetRoleByNameAsync(organizationId, request.Role, cancellationToken)).Id;
        }

        var duplicateGrant = await _dbContext.AccessGrants.AnyAsync(x =>
            x.OrganizationId == organizationId &&
            x.UserId == request.UserId &&
            x.TeamId == request.TeamId &&
            x.RoleId == roleId &&
            x.ResourceType == resourceType &&
            x.ResourceId == request.ResourceId &&
            x.PermissionKey == request.PermissionKey,
            cancellationToken);

        if (duplicateGrant)
        {
            throw new ConflictException("This access grant already exists.");
        }

        var grant = new AccessGrant
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            UserId = request.UserId,
            TeamId = request.TeamId,
            RoleId = roleId,
            ResourceType = resourceType,
            ResourceId = request.ResourceId,
            PermissionKey = request.PermissionKey,
            GrantedByUserId = userId,
            GrantedAtUtc = DateTime.UtcNow
        };

        _dbContext.AccessGrants.Add(grant);
        await AddAuditAsync(organizationId, userId, AuditLogAction.AccessGranted, resourceType, request.ResourceId, new { request.PermissionKey, request.UserId, request.TeamId, request.Role }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return ToAccessGrantDto(grant, request.Role);
    }

    public async Task<List<AccessGrantDto>> GetAccessGrantsAsync(Guid organizationId, string resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.FoldersManageAccess, organizationId: organizationId, cancellationToken: cancellationToken);
        if (!Enum.TryParse<AccessResourceType>(resourceType, true, out var parsed))
        {
            throw new BadRequestException("Unsupported resource type.");
        }

        var grants = await _dbContext.AccessGrants
            .AsNoTracking()
            .Include(x => x.Role)
            .Where(x => x.OrganizationId == organizationId && x.ResourceType == parsed && x.ResourceId == resourceId)
            .OrderByDescending(x => x.GrantedAtUtc)
            .ToListAsync(cancellationToken);

        return grants.Select(x => ToAccessGrantDto(x, x.Role?.Name)).ToList();
    }

    public async Task RevokeAccessAsync(Guid organizationId, Guid grantId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.FoldersManageAccess, organizationId: organizationId, cancellationToken: cancellationToken);
        var grant = await _dbContext.AccessGrants.FirstOrDefaultAsync(x => x.Id == grantId && x.OrganizationId == organizationId, cancellationToken)
            ?? throw new NotFoundException("Access grant not found.");
        _dbContext.AccessGrants.Remove(grant);
        await AddAuditAsync(organizationId, userId, AuditLogAction.AccessRevoked, grant.ResourceType, grant.ResourceId, new { grant.PermissionKey }, cancellationToken);
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<List<AuditLogDto>> GetAuditLogsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var userId = _currentUserService.GetUserId();
        await _permissionService.EnsurePermissionAsync(userId, PermissionKeys.AdminViewAuditLogs, organizationId: organizationId, cancellationToken: cancellationToken);

        return await _dbContext.AuditLogs
            .AsNoTracking()
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.OccurredAtUtc)
            .Take(200)
            .Select(x => new AuditLogDto
            {
                Id = x.Id,
                ActorUserId = x.ActorUserId,
                Action = x.Action.ToString(),
                ResourceType = x.ResourceType != null ? x.ResourceType.ToString() : null,
                ResourceId = x.ResourceId,
                DetailsJson = x.DetailsJson,
                OccurredAtUtc = x.OccurredAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task EnsureResourceBelongsToOrganizationAsync(Guid organizationId, AccessResourceType resourceType, Guid resourceId, CancellationToken cancellationToken)
    {
        var exists = resourceType switch
        {
            AccessResourceType.Organization => resourceId == organizationId &&
                await _dbContext.Organizations.AnyAsync(x => x.Id == organizationId && x.IsActive, cancellationToken),
            AccessResourceType.Folder => await _dbContext.DocumentFolders.AnyAsync(x => x.Id == resourceId && x.OrganizationId == organizationId, cancellationToken),
            AccessResourceType.Document => await _dbContext.Documents.AnyAsync(x => x.Id == resourceId && x.OrganizationId == organizationId, cancellationToken),
            AccessResourceType.AiActionTemplate => await _dbContext.AiActionTemplates.AnyAsync(x => x.Id == resourceId && x.OrganizationId == organizationId, cancellationToken),
            AccessResourceType.Workflow => true,
            _ => false
        };

        if (!exists)
        {
            throw new BadRequestException("Resource does not belong to this organization.");
        }
    }

    private async Task EnsurePermissionCatalogAsync(CancellationToken cancellationToken)
    {
        var existing = await _dbContext.Permissions.Select(x => x.Key).ToListAsync(cancellationToken);
        foreach (var permission in PermissionKeys.All)
        {
            if (!existing.Contains(permission.Key))
            {
                _dbContext.Permissions.Add(new Permission { Id = Guid.NewGuid(), Key = permission.Key, Description = permission.Value });
            }
        }
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    private async Task<List<Role>> CreateDefaultRolesAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var permissions = await _dbContext.Permissions.ToDictionaryAsync(x => x.Key, x => x.Id, cancellationToken);
        var roles = new List<Role>();

        foreach (var roleDefault in OrganizationRoleDefaults.PermissionsByRole)
        {
            var role = new Role
            {
                Id = Guid.NewGuid(),
                OrganizationId = organizationId,
                Name = roleDefault.Key,
                NormalizedName = NormalizeName(roleDefault.Key),
                Scope = "Organization",
                IsSystemRole = true,
                CreatedAtUtc = DateTime.UtcNow
            };

            foreach (var permissionKey in roleDefault.Value.Distinct())
            {
                if (permissions.TryGetValue(permissionKey, out var permissionId))
                {
                    role.Permissions.Add(new RolePermission { RoleId = role.Id, PermissionId = permissionId });
                }
            }

            roles.Add(role);
        }

        _dbContext.Roles.AddRange(roles);
        return roles;
    }

    private async Task<Role> GetRoleByNameAsync(Guid organizationId, string roleName, CancellationToken cancellationToken)
    {
        var normalized = NormalizeName(roleName);
        return await _dbContext.Roles.FirstOrDefaultAsync(x => x.OrganizationId == organizationId && x.NormalizedName == normalized, cancellationToken)
            ?? throw new BadRequestException($"Role '{roleName}' does not exist in this organization.");
    }

    private async Task<string> CreateUniqueSlugAsync(string name, CancellationToken cancellationToken)
    {
        var baseSlug = Slugify(name);
        var slug = baseSlug;
        var index = 2;
        while (await _dbContext.Organizations.AnyAsync(x => x.Slug == slug, cancellationToken))
        {
            slug = $"{baseSlug}-{index++}";
        }
        return slug;
    }

    private async Task<string> CreateUniqueInviteCodeAsync(CancellationToken cancellationToken)
    {
        for (var i = 0; i < 10; i++)
        {
            var code = $"ORG-{RandomNumberGenerator.GetInt32(100000, 999999)}";
            if (!await _dbContext.OrganizationInvitations.AnyAsync(x => x.Code == code, cancellationToken))
            {
                return code;
            }
        }
        return $"ORG-{Guid.NewGuid():N}"[..16].ToUpperInvariant();
    }

    private async Task AddAuditAsync(Guid organizationId, Guid actorUserId, AuditLogAction action, AccessResourceType resourceType, Guid resourceId, object? details, CancellationToken cancellationToken)
    {
        _dbContext.AuditLogs.Add(new AuditLog
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            ActorUserId = actorUserId,
            Action = action,
            ResourceType = resourceType,
            ResourceId = resourceId,
            DetailsJson = details is null ? null : JsonSerializer.Serialize(details),
            OccurredAtUtc = DateTime.UtcNow
        });
        await Task.CompletedTask;
    }

    private static OrganizationDto ToOrganizationDto(Organization organization, IEnumerable<string> roles)
    {
        return new OrganizationDto
        {
            Id = organization.Id,
            Name = organization.Name,
            Slug = organization.Slug,
            OwnerUserId = organization.OwnerUserId,
            IsActive = organization.IsActive,
            AllowPrivateDocuments = organization.AllowPrivateDocuments,
            AllowPublicShareLinks = organization.AllowPublicShareLinks,
            CreatedAtUtc = organization.CreatedAtUtc,
            CurrentUserRoles = roles.OrderBy(x => x).ToList()
        };
    }

    private static OrganizationMemberDto ToMemberDto(OrganizationMember member)
    {
        return new OrganizationMemberDto
        {
            Id = member.Id,
            UserId = member.UserId,
            Email = member.User.Email,
            DisplayName = member.User.DisplayName,
            Status = member.Status.ToString(),
            JoinedAtUtc = member.JoinedAtUtc,
            Roles = member.Roles.Select(x => x.Role.Name).OrderBy(x => x).ToList()
        };
    }

    private static OrganizationInvitationDto ToInvitationDto(OrganizationInvitation invitation)
    {
        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
            OrganizationId = invitation.OrganizationId,
            Email = invitation.Email,
            Code = invitation.Code,
            Status = invitation.Status.ToString(),
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc
        };
    }

    private static RoleDto ToRoleDto(Role role)
    {
        return new RoleDto
        {
            Id = role.Id,
            Name = role.Name,
            IsSystemRole = role.IsSystemRole,
            Permissions = role.Permissions.Select(x => x.Permission.Key).OrderBy(x => x).ToList()
        };
    }

    private static AccessGrantDto ToAccessGrantDto(AccessGrant grant, string? roleName)
    {
        return new AccessGrantDto
        {
            Id = grant.Id,
            UserId = grant.UserId,
            TeamId = grant.TeamId,
            Role = roleName,
            ResourceType = grant.ResourceType.ToString(),
            ResourceId = grant.ResourceId,
            PermissionKey = grant.PermissionKey,
            GrantedAtUtc = grant.GrantedAtUtc
        };
    }

    private static string NormalizeRequired(string? value, string fieldName)
    {
        var normalized = value?.Trim();
        if (string.IsNullOrWhiteSpace(normalized))
        {
            throw new BadRequestException($"{fieldName} is required.");
        }
        return normalized;
    }

    private static string NormalizeEmail(string value)
    {
        var email = NormalizeRequired(value, "Email").ToLowerInvariant();
        if (!email.Contains('@'))
        {
            throw new BadRequestException("Invalid email address.");
        }
        return email;
    }

    private static string NormalizeName(string value) => NormalizeRequired(value, "Name").Trim().ToUpperInvariant();

    private static string Slugify(string value)
    {
        var lower = value.Trim().ToLowerInvariant();
        var compact = Regex.Replace(lower, @"\s+", "-");
        var safe = Regex.Replace(compact, @"[^a-z0-9\-]", "");
        safe = Regex.Replace(safe, @"\-{2,}", "-").Trim('-');
        return string.IsNullOrWhiteSpace(safe) ? "organization" : safe;
    }

    private static string HashToken(string token)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(token));
        return Convert.ToHexString(bytes);
    }
}

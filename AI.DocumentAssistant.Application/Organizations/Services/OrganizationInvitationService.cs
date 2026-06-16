using System.Net.Mail;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Communication;
using AI.DocumentAssistant.Application.Abstractions.Organizations;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Organizations.Dtos;
using AI.DocumentAssistant.Domain.Entities;
using AI.DocumentAssistant.Domain.Enums;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Organizations.Services;

public sealed class OrganizationInvitationService : IOrganizationInvitationService
{
    private const int DefaultInvitationLifetimeDays = 3;
    private const int MinInvitationLifetimeDays = 1;
    private const int MaxInvitationLifetimeDays = 30;

    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;
    private readonly IEmailSender _emailSender;
    private readonly IOrganizationInvitationEmailTemplateService _emailTemplateService;

    public OrganizationInvitationService(
        IApplicationDbContext dbContext,
        ICurrentUserService currentUserService,
        IEmailSender emailSender,
        IOrganizationInvitationEmailTemplateService emailTemplateService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
        _emailSender = emailSender;
        _emailTemplateService = emailTemplateService;
    }

    public async Task<OrganizationInvitationDto> CreateInvitationAsync(
        Guid organizationId,
        CreateOrganizationInvitationDto dto,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        if (!organization.IsActive)
        {
            throw new BadRequestException("ORGANIZATION_INACTIVE_INVITATIONS_DISABLED");
        }

        var email = NormalizeEmail(dto.Email);
        var invitedUser = await _dbContext.Users
            .FirstOrDefaultAsync(x => x.Email == email, cancellationToken);

        if (invitedUser is null)
        {
            throw new NotFoundException("ORGANIZATION_INVITATION_USER_ACCOUNT_REQUIRED");
        }

        if (!invitedUser.IsActive)
        {
            throw new BadRequestException("ORGANIZATION_CANNOT_INVITE_INACTIVE_USER");
        }

        var alreadyMember = await _dbContext.OrganizationMembers
            .AnyAsync(x => x.OrganizationId == organizationId && x.UserId == invitedUser.Id && x.IsActive, cancellationToken);
        if (alreadyMember)
        {
            throw new ConflictException("ORGANIZATION_INVITATION_EMAIL_ALREADY_MEMBER");
        }

        var now = DateTime.UtcNow;
        var duplicatePending = await _dbContext.OrganizationInvitations
            .AnyAsync(x => x.OrganizationId == organizationId &&
                           x.Email == email &&
                           x.Status == OrganizationInvitationStatus.Pending &&
                           x.ExpiresAtUtc > now,
                cancellationToken);
        if (duplicatePending)
        {
            throw new ConflictException("ORGANIZATION_INVITATION_ALREADY_PENDING");
        }

        var settings = await EnsureSettingsAsync(organizationId, cancellationToken);
        var rawCode = GenerateInvitationCode();
        var invitation = new OrganizationInvitation
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            Email = email,
            InvitedUserId = invitedUser.Id,
            InvitedByUserId = currentUser.Id,
            CodeHash = ComputeSha256(NormalizeInvitationCode(rawCode)),
            Status = OrganizationInvitationStatus.Pending,
            CreatedAtUtc = now,
            ExpiresAtUtc = now.AddDays(settings.InvitationLifetimeDays)
        };

        var notification = new UserNotification
        {
            Id = Guid.NewGuid(),
            UserId = invitedUser.Id,
            Type = UserNotificationType.OrganizationInvitation,
            TitleKey = "notifications.organizationInvitation.title",
            MessageKey = "notifications.organizationInvitation.message",
            PayloadJson = JsonSerializer.Serialize(new
            {
                invitationId = invitation.Id,
                organizationId = organization.Id,
                organizationName = organization.Name,
                invitedByEmail = currentUser.Email,
                expiresAtUtc = invitation.ExpiresAtUtc
            }),
            CreatedAtUtc = now,
            RelatedOrganizationId = organization.Id,
            RelatedInvitationId = invitation.Id
        };

        _dbContext.OrganizationInvitations.Add(invitation);
        _dbContext.UserNotifications.Add(notification);
        AddActivityLog(organization.Id, currentUser.Id, OrganizationActivityActionType.OrganizationInvitationCreated, new
        {
            invitationId = invitation.Id,
            email,
            invitedUserId = invitedUser.Id,
            expiresAtUtc = invitation.ExpiresAtUtc
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        var message = _emailTemplateService.BuildInvitationEmail(
            invitedUser.PreferredLanguage,
            organization.Name,
            currentUser.Email,
            rawCode,
            invitation.ExpiresAtUtc);

        await _emailSender.SendAsync(invitedUser.Email, message.Subject, message.HtmlBody, cancellationToken);

        invitation.Organization = organization;
        invitation.InvitedUser = invitedUser;
        invitation.InvitedByUser = currentUser;
        return MapInvitation(invitation);
    }

    public async Task<IReadOnlyList<OrganizationInvitationDto>> GetOrganizationInvitationsAsync(
        Guid organizationId,
        bool includeHistory = false,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        var organization = await _dbContext.Organizations
            .Include(x => x.Members)
            .FirstAsync(x => x.Id == organizationId, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        var now = DateTime.UtcNow;
        var query = _dbContext.OrganizationInvitations
            .AsNoTracking()
            .Include(x => x.Organization)
            .Include(x => x.InvitedUser)
            .Include(x => x.InvitedByUser)
            .Where(x => x.OrganizationId == organizationId);

        if (!includeHistory)
        {
            query = query.Where(x => x.Status == OrganizationInvitationStatus.Pending && x.ExpiresAtUtc > now);
        }

        var invitations = await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .ToListAsync(cancellationToken);

        return invitations.Select(MapInvitation).ToList();
    }

    public async Task RevokeInvitationAsync(
        Guid organizationId,
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        var invitation = await _dbContext.OrganizationInvitations
            .FirstOrDefaultAsync(x => x.Id == invitationId && x.OrganizationId == organizationId, cancellationToken);

        if (invitation is null)
        {
            throw new NotFoundException("ORGANIZATION_INVITATION_NOT_FOUND");
        }

        if (invitation.Status != OrganizationInvitationStatus.Pending)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_NOT_ACTIVE");
        }

        invitation.Status = OrganizationInvitationStatus.Revoked;
        invitation.RevokedAtUtc = DateTime.UtcNow;
        invitation.RevokedByUserId = currentUser.Id;

        await DismissInvitationNotificationsAsync(invitation.Id, cancellationToken);
        AddActivityLog(organizationId, currentUser.Id, OrganizationActivityActionType.OrganizationInvitationRevoked, new
        {
            invitationId,
            invitation.Email
        });

        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task<OrganizationInvitationAcceptResultDto> AcceptInvitationAsync(
        Guid invitationId,
        CancellationToken cancellationToken = default)
    {
        var invitation = await _dbContext.OrganizationInvitations
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.Id == invitationId, cancellationToken);

        if (invitation is null)
        {
            throw new NotFoundException("ORGANIZATION_INVITATION_NOT_FOUND");
        }

        return await AcceptInvitationCoreAsync(invitation, cancellationToken);
    }

    public async Task<OrganizationInvitationAcceptResultDto> AcceptInvitationByCodeAsync(
        string code,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(code))
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_CODE_REQUIRED");
        }

        var codeHash = ComputeSha256(NormalizeInvitationCode(code));
        var invitation = await _dbContext.OrganizationInvitations
            .Include(x => x.Organization)
            .FirstOrDefaultAsync(x => x.CodeHash == codeHash, cancellationToken);

        if (invitation is null)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_CODE_INVALID");
        }

        return await AcceptInvitationCoreAsync(invitation, cancellationToken);
    }

    public async Task<OrganizationSettingsDto> GetSettingsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);
        var settings = await EnsureSettingsAsync(organizationId, cancellationToken);
        return new OrganizationSettingsDto
        {
            OrganizationId = settings.OrganizationId,
            InvitationLifetimeDays = settings.InvitationLifetimeDays
        };
    }

    public async Task<OrganizationSettingsDto> UpdateSettingsAsync(
        Guid organizationId,
        UpdateOrganizationSettingsDto dto,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        var organization = await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);
        EnsureCanManageOrganization(currentUser, organization);

        if (dto.InvitationLifetimeDays < MinInvitationLifetimeDays || dto.InvitationLifetimeDays > MaxInvitationLifetimeDays)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_LIFETIME_DAYS_INVALID");
        }

        var settings = await EnsureSettingsAsync(organizationId, cancellationToken);
        var previousInvitationLifetimeDays = settings.InvitationLifetimeDays;

        if (previousInvitationLifetimeDays == dto.InvitationLifetimeDays)
        {
            return new OrganizationSettingsDto
            {
                OrganizationId = settings.OrganizationId,
                InvitationLifetimeDays = settings.InvitationLifetimeDays
            };
        }

        settings.InvitationLifetimeDays = dto.InvitationLifetimeDays;
        settings.UpdatedAtUtc = DateTime.UtcNow;

        AddActivityLog(organizationId, currentUser.Id, OrganizationActivityActionType.OrganizationSettingsUpdated, new
        {
            // Keep both values in the journal payload so the UI can show an expandable change preview.
            previousInvitationLifetimeDays,
            newInvitationLifetimeDays = dto.InvitationLifetimeDays,
            changes = new
            {
                invitationLifetimeDays = new
                {
                    previousValue = previousInvitationLifetimeDays,
                    newValue = dto.InvitationLifetimeDays
                }
            }
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new OrganizationSettingsDto
        {
            OrganizationId = settings.OrganizationId,
            InvitationLifetimeDays = settings.InvitationLifetimeDays
        };
    }

    public async Task<IReadOnlyList<OrganizationActivityLogDto>> GetActivityLogsAsync(
        Guid organizationId,
        CancellationToken cancellationToken = default)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);
        await GetVisibleOrganizationAsync(organizationId, currentUser, cancellationToken);

        return await _dbContext.OrganizationActivityLogs
            .AsNoTracking()
            .Include(x => x.ActorUser)
            .Where(x => x.OrganizationId == organizationId)
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new OrganizationActivityLogDto
            {
                Id = x.Id,
                OrganizationId = x.OrganizationId,
                ActorUserId = x.ActorUserId,
                ActorEmail = x.ActorUser == null ? null : x.ActorUser.Email,
                ActionType = x.ActionType.ToString(),
                PayloadJson = x.PayloadJson,
                CreatedAtUtc = x.CreatedAtUtc
            })
            .ToListAsync(cancellationToken);
    }

    private async Task<OrganizationInvitationAcceptResultDto> AcceptInvitationCoreAsync(
        OrganizationInvitation invitation,
        CancellationToken cancellationToken)
    {
        var currentUser = await GetCurrentActiveUserAsync(cancellationToken);

        // Security rule: invitation codes are not transferable. The authenticated user's account email
        // must be the same email that received the invitation.
        if (invitation.InvitedUserId != currentUser.Id ||
            !string.Equals(invitation.Email, currentUser.Email, StringComparison.OrdinalIgnoreCase))
        {
            throw new ForbiddenException("ORGANIZATION_INVITATION_EMAIL_MISMATCH");
        }

        if (invitation.Status == OrganizationInvitationStatus.Revoked)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_REVOKED");
        }

        if (invitation.Status == OrganizationInvitationStatus.Accepted)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_ALREADY_ACCEPTED");
        }

        if (invitation.Status == OrganizationInvitationStatus.Expired || invitation.ExpiresAtUtc <= DateTime.UtcNow)
        {
            invitation.Status = OrganizationInvitationStatus.Expired;
            await DismissInvitationNotificationsAsync(invitation.Id, cancellationToken);
            await _dbContext.SaveChangesAsync(cancellationToken);
            throw new BadRequestException("ORGANIZATION_INVITATION_EXPIRED");
        }

        if (!invitation.Organization.IsActive)
        {
            throw new BadRequestException("ORGANIZATION_INVITATION_ORGANIZATION_INACTIVE");
        }

        var now = DateTime.UtcNow;
        var membership = await _dbContext.OrganizationMembers
            .FirstOrDefaultAsync(x => x.OrganizationId == invitation.OrganizationId && x.UserId == currentUser.Id, cancellationToken);

        if (membership is null)
        {
            membership = new OrganizationMember
            {
                Id = Guid.NewGuid(),
                OrganizationId = invitation.OrganizationId,
                UserId = currentUser.Id,
                AddedByUserId = invitation.InvitedByUserId,
                JoinedAtUtc = now,
                IsActive = true
            };
            _dbContext.OrganizationMembers.Add(membership);
        }
        else
        {
            membership.IsActive = true;
            membership.RemovedAtUtc = null;
            membership.JoinedAtUtc = now;
            membership.AddedByUserId = invitation.InvitedByUserId;
        }

        invitation.Status = OrganizationInvitationStatus.Accepted;
        invitation.AcceptedAtUtc = now;

        await DismissInvitationNotificationsAsync(invitation.Id, cancellationToken);
        AddActivityLog(invitation.OrganizationId, currentUser.Id, OrganizationActivityActionType.OrganizationInvitationAccepted, new
        {
            invitationId = invitation.Id,
            email = currentUser.Email
        });
        AddActivityLog(invitation.OrganizationId, currentUser.Id, OrganizationActivityActionType.OrganizationMemberJoined, new
        {
            userId = currentUser.Id,
            email = currentUser.Email
        });

        await _dbContext.SaveChangesAsync(cancellationToken);

        return new OrganizationInvitationAcceptResultDto
        {
            OrganizationId = invitation.OrganizationId,
            OrganizationName = invitation.Organization.Name,
            JoinedAtUtc = membership.JoinedAtUtc
        };
    }

    private async Task DismissInvitationNotificationsAsync(Guid invitationId, CancellationToken cancellationToken)
    {
        var notifications = await _dbContext.UserNotifications
            .Where(x => x.RelatedInvitationId == invitationId && x.DismissedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            notification.DismissedAtUtc = now;
            notification.ReadAtUtc ??= now;
        }
    }

    private async Task<OrganizationSettings> EnsureSettingsAsync(Guid organizationId, CancellationToken cancellationToken)
    {
        var settings = await _dbContext.OrganizationSettings
            .FirstOrDefaultAsync(x => x.OrganizationId == organizationId, cancellationToken);

        if (settings is not null)
        {
            return settings;
        }

        settings = new OrganizationSettings
        {
            Id = Guid.NewGuid(),
            OrganizationId = organizationId,
            InvitationLifetimeDays = DefaultInvitationLifetimeDays,
            CreatedAtUtc = DateTime.UtcNow
        };
        _dbContext.OrganizationSettings.Add(settings);
        await _dbContext.SaveChangesAsync(cancellationToken);
        return settings;
    }

    private async Task<User> GetCurrentActiveUserAsync(CancellationToken cancellationToken)
    {
        var currentUserId = _currentUserService.GetUserId();
        var currentUser = await _dbContext.Users.FirstOrDefaultAsync(x => x.Id == currentUserId, cancellationToken);
        if (currentUser is null || !currentUser.IsActive)
        {
            throw new UnauthorizedException("AUTH_USER_NOT_AUTHENTICATED");
        }

        return currentUser;
    }

    private async Task<Organization> GetVisibleOrganizationAsync(Guid organizationId, User currentUser, CancellationToken cancellationToken)
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
        if (currentUser.Role == UserRole.Manager && (organization.CreatedByUserId == currentUser.Id || isActiveMember))
        {
            return organization;
        }

        if (organization.IsActive && isActiveMember)
        {
            return organization;
        }

        throw new NotFoundException("ORGANIZATION_NOT_FOUND");
    }

    private static void EnsureCanManageOrganization(User currentUser, Organization organization)
    {
        if (currentUser.Role == UserRole.Admin) return;
        var isActiveMember = organization.Members.Any(x => x.UserId == currentUser.Id && x.IsActive);
        if (currentUser.Role == UserRole.Manager && (organization.CreatedByUserId == currentUser.Id || isActiveMember)) return;
        throw new ForbiddenException("ORGANIZATION_MANAGE_FORBIDDEN");
    }

    private static OrganizationInvitationDto MapInvitation(OrganizationInvitation invitation)
    {
        return new OrganizationInvitationDto
        {
            Id = invitation.Id,
            OrganizationId = invitation.OrganizationId,
            OrganizationName = invitation.Organization.Name,
            Email = invitation.Email,
            InvitedUserId = invitation.InvitedUserId,
            InvitedUserEmail = invitation.InvitedUser.Email,
            InvitedByUserId = invitation.InvitedByUserId,
            InvitedByEmail = invitation.InvitedByUser.Email,
            Status = invitation.ExpiresAtUtc <= DateTime.UtcNow && invitation.Status == OrganizationInvitationStatus.Pending
                ? OrganizationInvitationStatus.Expired.ToString()
                : invitation.Status.ToString(),
            CreatedAtUtc = invitation.CreatedAtUtc,
            ExpiresAtUtc = invitation.ExpiresAtUtc,
            AcceptedAtUtc = invitation.AcceptedAtUtc,
            RevokedAtUtc = invitation.RevokedAtUtc
        };
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

    private static string NormalizeEmail(string? email)
    {
        if (string.IsNullOrWhiteSpace(email) || !IsValidEmail(email))
        {
            throw new BadRequestException("AUTH_EMAIL_INVALID");
        }

        return email.Trim().ToLowerInvariant();
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            _ = new MailAddress(email);
            return true;
        }
        catch
        {
            return false;
        }
    }

    private static string GenerateInvitationCode()
    {
        const string alphabet = "ABCDEFGHJKLMNPQRSTUVWXYZ23456789";
        Span<byte> bytes = stackalloc byte[12];
        RandomNumberGenerator.Fill(bytes);
        var builder = new StringBuilder(14);
        for (var i = 0; i < bytes.Length; i++)
        {
            if (i is 4 or 8) builder.Append('-');
            builder.Append(alphabet[bytes[i] % alphabet.Length]);
        }
        return builder.ToString();
    }

    private static string NormalizeInvitationCode(string code)
    {
        return code.Trim().ToUpperInvariant().Replace(" ", string.Empty).Replace("-", string.Empty);
    }

    private static string ComputeSha256(string input)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(input));
        return Convert.ToHexString(bytes);
    }
}

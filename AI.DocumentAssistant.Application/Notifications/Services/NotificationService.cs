using AI.DocumentAssistant.Application.Abstractions.Common;
using AI.DocumentAssistant.Application.Abstractions.Notifications;
using AI.DocumentAssistant.Application.Common.Exceptions;
using AI.DocumentAssistant.Application.Notifications.Dtos;
using AI.DocumentAssistant.Infrastructure.Persistence.Abstractions;
using Microsoft.EntityFrameworkCore;

namespace AI.DocumentAssistant.Application.Notifications.Services;

public sealed class NotificationService : INotificationService
{
    private readonly IApplicationDbContext _dbContext;
    private readonly ICurrentUserService _currentUserService;

    public NotificationService(IApplicationDbContext dbContext, ICurrentUserService currentUserService)
    {
        _dbContext = dbContext;
        _currentUserService = currentUserService;
    }

    public async Task<IReadOnlyList<UserNotificationDto>> GetMyNotificationsAsync(
        bool includeRead = false,
        CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();
        var query = _dbContext.UserNotifications
            .AsNoTracking()
            .Where(x => x.UserId == userId && x.DismissedAtUtc == null);

        if (!includeRead)
        {
            query = query.Where(x => x.ReadAtUtc == null);
        }

        return await query
            .OrderByDescending(x => x.CreatedAtUtc)
            .Take(100)
            .Select(x => new UserNotificationDto
            {
                Id = x.Id,
                Type = x.Type.ToString(),
                TitleKey = x.TitleKey,
                MessageKey = x.MessageKey,
                PayloadJson = x.PayloadJson,
                CreatedAtUtc = x.CreatedAtUtc,
                ReadAtUtc = x.ReadAtUtc,
                RelatedOrganizationId = x.RelatedOrganizationId,
                RelatedInvitationId = x.RelatedInvitationId
            })
            .ToListAsync(cancellationToken);
    }

    public async Task<int> GetMyUnreadCountAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();
        return await _dbContext.UserNotifications
            .CountAsync(x => x.UserId == userId && x.ReadAtUtc == null && x.DismissedAtUtc == null, cancellationToken);
    }

    public async Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();
        var notification = await _dbContext.UserNotifications
            .FirstOrDefaultAsync(x => x.Id == notificationId && x.UserId == userId, cancellationToken);

        if (notification is null || notification.DismissedAtUtc is not null)
        {
            throw new NotFoundException("NOTIFICATION_NOT_FOUND");
        }

        notification.ReadAtUtc ??= DateTime.UtcNow;
        await _dbContext.SaveChangesAsync(cancellationToken);
    }

    public async Task MarkAllAsReadAsync(CancellationToken cancellationToken = default)
    {
        var userId = _currentUserService.GetUserId();
        var notifications = await _dbContext.UserNotifications
            .Where(x => x.UserId == userId && x.ReadAtUtc == null && x.DismissedAtUtc == null)
            .ToListAsync(cancellationToken);

        var now = DateTime.UtcNow;
        foreach (var notification in notifications)
        {
            notification.ReadAtUtc = now;
        }

        await _dbContext.SaveChangesAsync(cancellationToken);
    }
}

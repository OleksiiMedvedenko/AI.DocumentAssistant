using AI.DocumentAssistant.Application.Notifications.Dtos;

namespace AI.DocumentAssistant.Application.Abstractions.Notifications;

public interface INotificationService
{
    Task<IReadOnlyList<UserNotificationDto>> GetMyNotificationsAsync(bool includeRead = false, CancellationToken cancellationToken = default);
    Task<int> GetMyUnreadCountAsync(CancellationToken cancellationToken = default);
    Task MarkAsReadAsync(Guid notificationId, CancellationToken cancellationToken = default);
    Task MarkAllAsReadAsync(CancellationToken cancellationToken = default);
}

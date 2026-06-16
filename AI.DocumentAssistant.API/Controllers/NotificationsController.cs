using AI.DocumentAssistant.API.Contracts.Notifications;
using AI.DocumentAssistant.Application.Abstractions.Notifications;
using AI.DocumentAssistant.Application.Notifications.Dtos;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace AI.DocumentAssistant.API.Controllers;

[ApiController]
[Authorize]
[Route("api/notifications")]
public sealed class NotificationsController : ControllerBase
{
    private readonly INotificationService _notificationService;

    public NotificationsController(INotificationService notificationService)
    {
        _notificationService = notificationService;
    }

    [HttpGet]
    public async Task<IActionResult> GetMyNotifications(
        [FromQuery] bool includeRead,
        CancellationToken cancellationToken)
    {
        var result = await _notificationService.GetMyNotificationsAsync(includeRead, cancellationToken);
        return Ok(result.Select(Map));
    }

    [HttpGet("unread-count")]
    public async Task<IActionResult> GetUnreadCount(CancellationToken cancellationToken)
    {
        var count = await _notificationService.GetMyUnreadCountAsync(cancellationToken);
        return Ok(new { count });
    }

    [HttpPatch("{notificationId:guid}/read")]
    public async Task<IActionResult> MarkAsRead(Guid notificationId, CancellationToken cancellationToken)
    {
        await _notificationService.MarkAsReadAsync(notificationId, cancellationToken);
        return NoContent();
    }

    [HttpPatch("read-all")]
    public async Task<IActionResult> MarkAllAsRead(CancellationToken cancellationToken)
    {
        await _notificationService.MarkAllAsReadAsync(cancellationToken);
        return NoContent();
    }

    private static UserNotificationResponse Map(UserNotificationDto dto)
    {
        return new UserNotificationResponse
        {
            Id = dto.Id,
            Type = dto.Type,
            TitleKey = dto.TitleKey,
            MessageKey = dto.MessageKey,
            PayloadJson = dto.PayloadJson,
            CreatedAtUtc = dto.CreatedAtUtc,
            ReadAtUtc = dto.ReadAtUtc,
            RelatedOrganizationId = dto.RelatedOrganizationId,
            RelatedInvitationId = dto.RelatedInvitationId
        };
    }
}

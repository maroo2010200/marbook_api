using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Hubs;
using MarbookApi.Models;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging;

namespace MarbookApi.Services;

public class NotificationService(
    AppDbContext dbContext,
    IHubContext<NotificationHub> hubContext,
    ILogger<NotificationService> logger) : INotificationService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IHubContext<NotificationHub> _hubContext = hubContext;
    private readonly ILogger<NotificationService> _logger = logger;

    public async Task CreateNotificationAsync(
        int recipientUserId,
        int actorUserId,
        NotificationType type,
        int? targetPostId = null,
        int? targetCommentId = null)
    {
        // 1. Self-notification prevention: never notify yourself!
        if (actorUserId == recipientUserId)
        {
            return;
        }

        try
        {
            // 2. Prevent duplicate unread notifications for identical actions
            var existingUnread = await _dbContext.Notifications
                .FirstOrDefaultAsync(n =>
                    n.RecipientUserId == recipientUserId &&
                    n.ActorUserId == actorUserId &&
                    n.Type == type &&
                    n.TargetPostId == targetPostId &&
                    n.TargetCommentId == targetCommentId &&
                    !n.IsRead);

            Notification notification;

            if (existingUnread != null)
            {
                // Already notified and unread, bump timestamp
                existingUnread.CreatedAt = DateTime.UtcNow;
                await _dbContext.SaveChangesAsync();
                notification = existingUnread;
            }
            else
            {
                notification = new Notification
                {
                    RecipientUserId = recipientUserId,
                    ActorUserId = actorUserId,
                    Type = type,
                    TargetPostId = targetPostId,
                    TargetCommentId = targetCommentId,
                    IsRead = false,
                    CreatedAt = DateTime.UtcNow
                };

                _dbContext.Notifications.Add(notification);
                await _dbContext.SaveChangesAsync();
            }

            _logger.LogInformation(
                "Created {Type} notification from user {ActorId} to user {RecipientId}",
                type, actorUserId, recipientUserId);

            // 3. Real-time push via SignalR
            var actorUser = await _dbContext.Users.FindAsync(actorUserId);
            var dto = new NotificationResponseDto
            {
                Id = notification.Id,
                RecipientUserId = notification.RecipientUserId,
                ActorUserId = notification.ActorUserId,
                ActorName = actorUser?.Name ?? string.Empty,
                ActorAvatarUrl = actorUser?.AvatarUrl,
                Type = notification.Type.ToString(),
                TargetPostId = notification.TargetPostId,
                TargetCommentId = notification.TargetCommentId,
                IsRead = notification.IsRead,
                CreatedAt = notification.CreatedAt
            };

            var unreadCount = await _dbContext.Notifications
                .CountAsync(n => n.RecipientUserId == recipientUserId && !n.IsRead);

            await _hubContext.Clients.User(recipientUserId.ToString())
                .SendAsync("ReceiveNotification", dto, unreadCount);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to create notification {Type} for user {RecipientId}", type, recipientUserId);
        }
    }

    public async Task DeleteNotificationAsync(
        int recipientUserId,
        int actorUserId,
        NotificationType type,
        int? targetPostId = null,
        int? targetCommentId = null)
    {
        try
        {
            var notification = await _dbContext.Notifications
                .FirstOrDefaultAsync(n =>
                    n.RecipientUserId == recipientUserId &&
                    n.ActorUserId == actorUserId &&
                    n.Type == type &&
                    n.TargetPostId == targetPostId &&
                    n.TargetCommentId == targetCommentId &&
                    !n.IsRead);

            if (notification != null)
            {
                _dbContext.Notifications.Remove(notification);
                await _dbContext.SaveChangesAsync();

                _logger.LogInformation(
                    "Removed unread {Type} notification from user {ActorId} to user {RecipientId}",
                    type, actorUserId, recipientUserId);

                var unreadCount = await _dbContext.Notifications
                    .CountAsync(n => n.RecipientUserId == recipientUserId && !n.IsRead);

                await _hubContext.Clients.User(recipientUserId.ToString())
                    .SendAsync("NotificationDeleted", notification.Id, unreadCount);
            }
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to delete notification {Type} for user {RecipientId}", type, recipientUserId);
        }
    }
}

using MarbookApi.Models;

namespace MarbookApi.Services;

public interface INotificationService
{
    Task CreateNotificationAsync(
        int recipientUserId,
        int actorUserId,
        NotificationType type,
        int? targetPostId = null,
        int? targetCommentId = null);

    Task DeleteNotificationAsync(
        int recipientUserId,
        int actorUserId,
        NotificationType type,
        int? targetPostId = null,
        int? targetCommentId = null);
}

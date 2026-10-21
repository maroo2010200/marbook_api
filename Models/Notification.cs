namespace MarbookApi.Models;

public enum NotificationType
{
    PostLiked = 1,
    CommentLiked = 2,
    CommentCreated = 3,
    ReplyCreated = 4,
    UserFollowed = 5
}

public class Notification
{
    public int Id { get; set; }

    public int RecipientUserId { get; set; }
    public User RecipientUser { get; set; } = null!;

    public int ActorUserId { get; set; }
    public User ActorUser { get; set; } = null!;

    public NotificationType Type { get; set; }

    public int? TargetPostId { get; set; }
    public Post? TargetPost { get; set; }

    public int? TargetCommentId { get; set; }
    public Comment? TargetComment { get; set; }

    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

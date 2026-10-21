namespace MarbookApi.DTOs;

public class NotificationResponseDto
{
    public int Id { get; set; }
    public int RecipientUserId { get; set; }
    public int ActorUserId { get; set; }
    public string ActorName { get; set; } = string.Empty;
    public string? ActorAvatarUrl { get; set; }
    public string Type { get; set; } = string.Empty;
    public int? TargetPostId { get; set; }
    public int? TargetCommentId { get; set; }
    public bool IsRead { get; set; }
    public DateTime CreatedAt { get; set; }
}

public class UnreadCountDto
{
    public int UnreadCount { get; set; }
}

namespace MarbookApi.DTOs;

public class CommentResponseDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string? ImageUrl { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public string UserName { get; set; } = string.Empty;
    public string? UserAvatarUrl { get; set; }
    public int UserId { get; set; }
    public int? ParentCommentId { get; set; }
    public int LikeCount { get; set; }
    public int ReplyCount { get; set; }
    public bool Liked { get; set; }
}

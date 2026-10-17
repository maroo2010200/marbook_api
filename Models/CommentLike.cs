namespace MarbookApi.Models;

public class CommentLike
{
    public int Id { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public int CommentId { get; set; }
    public Comment Comment { get; set; } = null!;

    public int UserId { get; set; }
    public User User { get; set; } = null!;
}

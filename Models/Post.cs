namespace MarbookApi.Models;

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
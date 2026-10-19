namespace MarbookApi.Models;

public class Post
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public PostVisibility Visibility { get; set; } = PostVisibility.Public;
    public int UserId { get; set; }
    public User User { get; set; } = null!;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }

    public ICollection<Comment> Comments { get; set; } = new List<Comment>();
    public ICollection<Like> Likes { get; set; } = new List<Like>();
    public ICollection<PostImage> Images { get; set; } = new List<PostImage>();
}

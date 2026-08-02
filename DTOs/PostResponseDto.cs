
namespace MarbookApi.DTOs;
public class PostResponseDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public int UserId { get; set; }
    public string UserName { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
}
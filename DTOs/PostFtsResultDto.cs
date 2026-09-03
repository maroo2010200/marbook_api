namespace MarbookApi.DTOs;

public class PostFtsResultDto
{
    public int Id { get; set; }
    public string Content { get; set; } = string.Empty;
    public string UserName { get; set; } = string.Empty;
    public int UserId { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime UpdatedAt { get; set; }
    public double RelevanceScore { get; set; }
}
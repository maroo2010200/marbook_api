namespace MarbookApi.DTOs;

public class RepostResponseDto
{
    public bool IsReposted { get; set; }
    public int RepostsCount { get; set; }
    public PostResponseDto? Post { get; set; }
}

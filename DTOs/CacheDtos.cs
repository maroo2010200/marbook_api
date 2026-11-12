using MarbookApi.Models;

namespace MarbookApi.DTOs
{
    public record CachedUserProfileData(
        int Id,
        string Name,
        string? Username,
        string? Bio,
        string? AvatarUrl,
        string? CoverUrl,
        DateOnly Birthdate,
        bool IsBirthdatePrivate,
        DateTime CreatedAt,
        int FollowersCount,
        int FollowingCount,
        int PostsCount
    );

    public record CachedPostData(
        int Id,
        string Content,
        PostVisibility Visibility,
        int UserId,
        string UserName,
        string? UserAvatarUrl,
        DateTime CreatedAt,
        DateTime UpdatedAt,
        int LikesCount,
        int CommentsCount,
        List<string> ImageUrls,
        int? OriginalPostId = null,
        PostResponseDto? OriginalPost = null,
        int RepostsCount = 0,
        bool IsPinned = false
    );
}

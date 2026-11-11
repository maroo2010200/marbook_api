using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PostsController(
        AppDbContext dbContext,
        ILogger<PostsController> logger,
        HybridCache cache,
        INotificationService notificationService) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly ILogger _logger = logger;
        private readonly HybridCache _cache = cache;
        private readonly INotificationService _notificationService = notificationService;

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<PostResponseDto>>> GetPosts(
            [FromQuery] PaginationParams pagination,
            [FromQuery] string? search,
            [FromQuery] int? userId,
            [FromQuery] bool? followingOnly)
        {
            int? currentUserId = User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var query = _dbContext.Posts
                .Include(p => p.User)
                .Include(p => p.Images)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.User)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.Images)
                .AsQueryable();

            if (userId.HasValue)
            {
                query = query.Where(p => p.UserId == userId.Value);
            }

            if (currentUserId.HasValue)
            {
                var followedUserIds = await _dbContext.Follows
                    .Where(f => f.FollowerId == currentUserId.Value)
                    .Select(f => f.FolloweeId)
                    .ToListAsync();

                if (followingOnly == true)
                {
                    query = query.Where(p =>
                        followedUserIds.Contains(p.UserId) &&
                        (p.Visibility == PostVisibility.Public || p.Visibility == PostVisibility.Followers));
                }
                else
                {
                    query = query.Where(p =>
                        p.UserId == currentUserId.Value
                        || p.Visibility == PostVisibility.Public
                        || (p.Visibility == PostVisibility.Followers && followedUserIds.Contains(p.UserId)));
                }
            }
            else
            {
                if (followingOnly == true)
                {
                    return Ok(new PagedResult<PostResponseDto>
                    {
                        Items = new List<PostResponseDto>(),
                        TotalCount = 0,
                        PageNumber = pagination.PageNumber,
                        PageSize = pagination.PageSize
                    });
                }

                query = query.Where(p => p.Visibility == PostVisibility.Public);
            }

            if (!string.IsNullOrWhiteSpace(search))
            {
                query = query.Where(p => EF.Functions.ILike(p.Content, $"%{search}%"));
            }

            query = query.OrderByDescending(p => p.CreatedAt);

            var totalCount = await query.CountAsync();

            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var posts = await query
                .Skip(skip)
                .Take(pagination.PageSize)
                .Select(p => new PostResponseDto
                {
                    Id = p.Id,
                    Content = p.Content,
                    Visibility = p.Visibility,
                    UserName = p.User.Name,
                    UserAvatarUrl = p.User.AvatarUrl,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId,
                    LikesCount = p.Likes.Count(),
                    CommentsCount = p.Comments.Count(),
                    IsLiked = currentUserId.HasValue && p.Likes.Any(l => l.UserId == currentUserId.Value),
                    ImageUrls = p.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                    OriginalPostId = p.OriginalPostId,
                    OriginalPost = p.OriginalPost == null ? null : new PostResponseDto
                    {
                        Id = p.OriginalPost.Id,
                        Content = p.OriginalPost.Content,
                        Visibility = p.OriginalPost.Visibility,
                        UserName = p.OriginalPost.User.Name,
                        UserAvatarUrl = p.OriginalPost.User.AvatarUrl,
                        CreatedAt = p.OriginalPost.CreatedAt,
                        UpdatedAt = p.OriginalPost.UpdatedAt,
                        UserId = p.OriginalPost.UserId,
                        LikesCount = p.OriginalPost.Likes.Count(),
                        CommentsCount = p.OriginalPost.Comments.Count(),
                        ImageUrls = p.OriginalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
                    },
                    RepostsCount = p.Reposts.Count(),
                    IsReposted = currentUserId.HasValue && p.Reposts.Any(r => r.UserId == currentUserId.Value)
                })
                .ToListAsync();

            return Ok(new PagedResult<PostResponseDto>
            {
                Items = posts,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpGet("search")]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<PostFtsResultDto>>> SearchPosts([FromQuery] PaginationParams pagination, [FromQuery] string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return BadRequest("Search term cannot be empty.");
            }

            int? currentUserId = User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var query = _dbContext.Posts
                .Include(p => p.User)
                .Include(p => p.Images)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.User)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.Images)
                .Where(p => EF.Functions.ToTsVector("english", p.Content).Matches(EF.Functions.WebSearchToTsQuery("english", search)));

            if (currentUserId.HasValue)
            {
                var followedUserIds = await _dbContext.Follows
                    .Where(f => f.FollowerId == currentUserId.Value)
                    .Select(f => f.FolloweeId)
                    .ToListAsync();

                query = query.Where(p =>
                    p.UserId == currentUserId.Value
                    || p.Visibility == PostVisibility.Public
                    || (p.Visibility == PostVisibility.Followers && followedUserIds.Contains(p.UserId)));
            }
            else
            {
                query = query.Where(p => p.Visibility == PostVisibility.Public);
            }

            var totalCount = await query.CountAsync();

            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var posts = await query
                .Select(p => new PostFtsResultDto
                {
                    Id = p.Id,
                    Content = p.Content,
                    Visibility = p.Visibility,
                    UserName = p.User.Name,
                    UserAvatarUrl = p.User.AvatarUrl,
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId,
                    RelevanceScore = EF.Functions.ToTsVector("english", p.Content)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", search)),
                    LikesCount = p.Likes.Count(),
                    CommentsCount = p.Comments.Count(),
                    IsLiked = currentUserId.HasValue && p.Likes.Any(l => l.UserId == currentUserId.Value),
                    ImageUrls = p.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                    OriginalPostId = p.OriginalPostId,
                    OriginalPost = p.OriginalPost == null ? null : new PostResponseDto
                    {
                        Id = p.OriginalPost.Id,
                        Content = p.OriginalPost.Content,
                        Visibility = p.OriginalPost.Visibility,
                        UserName = p.OriginalPost.User.Name,
                        UserAvatarUrl = p.OriginalPost.User.AvatarUrl,
                        CreatedAt = p.OriginalPost.CreatedAt,
                        UpdatedAt = p.OriginalPost.UpdatedAt,
                        UserId = p.OriginalPost.UserId,
                        LikesCount = p.OriginalPost.Likes.Count(),
                        CommentsCount = p.OriginalPost.Comments.Count(),
                        ImageUrls = p.OriginalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
                    },
                    RepostsCount = p.Reposts.Count(),
                    IsReposted = currentUserId.HasValue && p.Reposts.Any(r => r.UserId == currentUserId.Value)
                })
                .OrderByDescending(p => p.RelevanceScore)
                .ThenByDescending(p => p.CreatedAt)
                .Skip(skip)
                .Take(pagination.PageSize)
                .ToListAsync();

            return Ok(new PagedResult<PostFtsResultDto>
            {
                Items = posts,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpGet("{id:int}")]
        [AllowAnonymous]
        public async Task<ActionResult<PostResponseDto>> GetPost(int id)
        {
            int? currentUserId = User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var post = await _cache.GetOrCreateAsync(
                $"post:{id}",
                async cancel =>
                {
                    var p = await _dbContext.Posts
                        .Include(x => x.User)
                        .Include(x => x.Likes)
                        .Include(x => x.Comments)
                        .Include(x => x.Images)
                        .Include(x => x.Reposts)
                        .Include(x => x.OriginalPost)
                            .ThenInclude(op => op!.User)
                        .Include(x => x.OriginalPost)
                            .ThenInclude(op => op!.Images)
                        .FirstOrDefaultAsync(x => x.Id == id, cancel);

                    if (p == null)
                    {
                        return null;
                    }

                    PostResponseDto? originalPostDto = null;
                    if (p.OriginalPost != null)
                    {
                        originalPostDto = new PostResponseDto
                        {
                            Id = p.OriginalPost.Id,
                            Content = p.OriginalPost.Content,
                            Visibility = p.OriginalPost.Visibility,
                            UserName = p.OriginalPost.User.Name,
                            UserAvatarUrl = p.OriginalPost.User.AvatarUrl,
                            CreatedAt = p.OriginalPost.CreatedAt,
                            UpdatedAt = p.OriginalPost.UpdatedAt,
                            UserId = p.OriginalPost.UserId,
                            LikesCount = p.OriginalPost.Likes.Count,
                            CommentsCount = p.OriginalPost.Comments.Count,
                            ImageUrls = p.OriginalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
                        };
                    }

                    return new CachedPostData(
                        p.Id,
                        p.Content,
                        p.Visibility,
                        p.UserId,
                        p.User.Name,
                        p.User.AvatarUrl,
                        p.CreatedAt,
                        p.UpdatedAt,
                        p.Likes.Count,
                        p.Comments.Count,
                        p.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                        p.OriginalPostId,
                        originalPostDto,
                        p.Reposts.Count
                    );
                },
                tags: [$"post-{id}"],
                cancellationToken: HttpContext.RequestAborted);

            if (post == null)
            {
                return NotFound();
            }

            if (post.Visibility != PostVisibility.Public)
            {
                if (!currentUserId.HasValue)
                {
                    return NotFound();
                }

                if (post.UserId != currentUserId.Value)
                {
                    if (post.Visibility == PostVisibility.Private)
                    {
                        return NotFound();
                    }

                    if (post.Visibility == PostVisibility.Followers)
                    {
                        var isFollower = await _dbContext.Follows
                            .AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == post.UserId);

                        if (!isFollower)
                        {
                            return NotFound();
                        }
                    }
                }
            }

            var isLiked = currentUserId.HasValue && await _dbContext.Likes.AnyAsync(l => l.PostId == id && l.UserId == currentUserId.Value);
            var isReposted = currentUserId.HasValue && await _dbContext.Posts.AnyAsync(rp => rp.OriginalPostId == id && rp.UserId == currentUserId.Value);

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = post.UserName,
                UserAvatarUrl = post.UserAvatarUrl,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = post.LikesCount,
                CommentsCount = post.CommentsCount,
                IsLiked = isLiked,
                ImageUrls = post.ImageUrls,
                OriginalPostId = post.OriginalPostId,
                OriginalPost = post.OriginalPost,
                RepostsCount = post.RepostsCount,
                IsReposted = isReposted
            };

            return Ok(responseDto);
        }

        [HttpPost]
        [EnableRateLimiting("post-policy")]
        public async Task<ActionResult<PostResponseDto>> CreatePost(PostCreateDto postDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            _logger.LogInformation("User {UserId} is creating a post with visibility {Visibility}.", userId, postDto.Visibility);

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning("User {UserId} not found when creating a post.", userId);
                return NotFound($"User with ID {userId} not found.");
            }

            var trimmedContent = postDto.Content?.Trim() ?? string.Empty;
            var hasImages = postDto.ImageUrls != null && postDto.ImageUrls.Any(u => !string.IsNullOrWhiteSpace(u));
            var hasOriginalPost = postDto.OriginalPostId.HasValue;

            if (string.IsNullOrWhiteSpace(trimmedContent) && !hasImages && !hasOriginalPost)
            {
                return BadRequest("Post must contain text content, at least one image, or reference a repost.");
            }

            Post? originalPost = null;
            int? rootPostId = null;
            if (hasOriginalPost)
            {
                var targetPost = await _dbContext.Posts
                    .Include(p => p.User)
                    .Include(p => p.Images)
                    .Include(p => p.OriginalPost)
                        .ThenInclude(op => op!.User)
                    .Include(p => p.OriginalPost)
                        .ThenInclude(op => op!.Images)
                    .FirstOrDefaultAsync(p => p.Id == postDto.OriginalPostId!.Value);

                if (targetPost == null)
                {
                    return NotFound("Original post not found.");
                }

                if (targetPost.Visibility == PostVisibility.Private && targetPost.UserId != userId)
                {
                    return BadRequest("Private posts cannot be reposted.");
                }

                if (targetPost.Visibility == PostVisibility.Followers && targetPost.UserId != userId)
                {
                    var isFollower = await _dbContext.Follows
                        .AnyAsync(f => f.FollowerId == userId && f.FolloweeId == targetPost.UserId);
                    if (!isFollower)
                    {
                        return Forbid();
                    }
                }

                rootPostId = targetPost.OriginalPostId ?? targetPost.Id;
                originalPost = targetPost.OriginalPostId.HasValue && targetPost.OriginalPost != null
                    ? targetPost.OriginalPost
                    : targetPost;
            }

            var post = new Post
            {
                Content = trimmedContent,
                Visibility = postDto.Visibility,
                UserId = userId,
                OriginalPostId = rootPostId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            if (postDto.ImageUrls != null && postDto.ImageUrls.Count > 0)
            {
                var order = 0;
                foreach (var url in postDto.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    post.Images.Add(new PostImage
                    {
                        Url = url.Trim(),
                        Order = order++,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            _dbContext.Posts.Add(post);
            await _dbContext.SaveChangesAsync();

            // Invalidate user profile cache (posts count changed)
            await _cache.RemoveByTagAsync($"user-{userId}");

            if (rootPostId.HasValue)
            {
                await _cache.RemoveByTagAsync($"post-{rootPostId.Value}");
                if (originalPost != null && originalPost.UserId != userId)
                {
                    await _notificationService.CreateNotificationAsync(
                        originalPost.UserId,
                        userId,
                        NotificationType.PostReposted,
                        rootPostId.Value);
                }
            }

            _logger.LogInformation("Post {PostId} created successfully by user {UserId}.", post.Id, userId);

            PostResponseDto? originalPostDto = null;
            if (originalPost != null)
            {
                originalPostDto = new PostResponseDto
                {
                    Id = originalPost.Id,
                    Content = originalPost.Content,
                    Visibility = originalPost.Visibility,
                    UserName = originalPost.User.Name,
                    UserAvatarUrl = originalPost.User.AvatarUrl,
                    CreatedAt = originalPost.CreatedAt,
                    UpdatedAt = originalPost.UpdatedAt,
                    UserId = originalPost.UserId,
                    LikesCount = await _dbContext.Likes.CountAsync(l => l.PostId == originalPost.Id),
                    CommentsCount = await _dbContext.Comments.CountAsync(c => c.PostId == originalPost.Id),
                    IsLiked = await _dbContext.Likes.AnyAsync(l => l.PostId == originalPost.Id && l.UserId == userId),
                    ImageUrls = originalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
                };
            }

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = user.Name,
                UserAvatarUrl = user.AvatarUrl,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = 0,
                CommentsCount = 0,
                IsLiked = false,
                ImageUrls = post.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                OriginalPostId = rootPostId,
                OriginalPost = originalPostDto,
                RepostsCount = 0,
                IsReposted = false
            };

            return CreatedAtAction(nameof(GetPost), new { id = post.Id }, responseDto);
        }

        [HttpPost("{id:int}/repost")]
        public async Task<ActionResult<RepostResponseDto>> ToggleRepost(int id)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var targetPost = await _dbContext.Posts
                .Include(p => p.User)
                .Include(p => p.Images)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.User)
                .Include(p => p.OriginalPost)
                    .ThenInclude(op => op!.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (targetPost == null)
            {
                return NotFound("Post not found.");
            }

            if (targetPost.Visibility == PostVisibility.Private && targetPost.UserId != userId)
            {
                return BadRequest("Private posts cannot be reposted.");
            }

            if (targetPost.Visibility == PostVisibility.Followers && targetPost.UserId != userId)
            {
                var isFollower = await _dbContext.Follows
                    .AnyAsync(f => f.FollowerId == userId && f.FolloweeId == targetPost.UserId);
                if (!isFollower)
                {
                    return Forbid();
                }
            }

            var rootPostId = targetPost.OriginalPostId ?? targetPost.Id;
            var originalPost = targetPost.OriginalPostId.HasValue && targetPost.OriginalPost != null
                ? targetPost.OriginalPost
                : targetPost;

            var existingRepost = await _dbContext.Posts
                .FirstOrDefaultAsync(p => p.UserId == userId && p.OriginalPostId == rootPostId);

            if (existingRepost != null)
            {
                // Un-repost
                _dbContext.Posts.Remove(existingRepost);
                await _dbContext.SaveChangesAsync();

                await _cache.RemoveByTagAsync($"post-{rootPostId}");
                await _cache.RemoveByTagAsync($"post-{existingRepost.Id}");
                await _cache.RemoveByTagAsync($"user-{userId}");

                if (originalPost.UserId != userId)
                {
                    await _notificationService.DeleteNotificationAsync(
                        originalPost.UserId,
                        userId,
                        NotificationType.PostReposted,
                        rootPostId);
                }

                var count = await _dbContext.Posts.CountAsync(p => p.OriginalPostId == rootPostId);

                return Ok(new RepostResponseDto
                {
                    IsReposted = false,
                    RepostsCount = count,
                    Post = null
                });
            }

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            var repost = new Post
            {
                UserId = userId,
                OriginalPostId = rootPostId,
                Content = string.Empty,
                Visibility = PostVisibility.Public,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Posts.Add(repost);
            await _dbContext.SaveChangesAsync();

            await _cache.RemoveByTagAsync($"post-{rootPostId}");
            await _cache.RemoveByTagAsync($"user-{userId}");

            if (originalPost.UserId != userId)
            {
                await _notificationService.CreateNotificationAsync(
                    originalPost.UserId,
                    userId,
                    NotificationType.PostReposted,
                    rootPostId);
            }

            var newRepostsCount = await _dbContext.Posts.CountAsync(p => p.OriginalPostId == rootPostId);

            var originalPostDto = new PostResponseDto
            {
                Id = originalPost.Id,
                Content = originalPost.Content,
                Visibility = originalPost.Visibility,
                UserName = originalPost.User.Name,
                UserAvatarUrl = originalPost.User.AvatarUrl,
                CreatedAt = originalPost.CreatedAt,
                UpdatedAt = originalPost.UpdatedAt,
                UserId = originalPost.UserId,
                LikesCount = await _dbContext.Likes.CountAsync(l => l.PostId == originalPost.Id),
                CommentsCount = await _dbContext.Comments.CountAsync(c => c.PostId == originalPost.Id),
                IsLiked = await _dbContext.Likes.AnyAsync(l => l.PostId == originalPost.Id && l.UserId == userId),
                ImageUrls = originalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
            };

            var repostDto = new PostResponseDto
            {
                Id = repost.Id,
                Content = repost.Content,
                Visibility = repost.Visibility,
                UserName = user.Name,
                UserAvatarUrl = user.AvatarUrl,
                CreatedAt = repost.CreatedAt,
                UpdatedAt = repost.UpdatedAt,
                UserId = user.Id,
                LikesCount = 0,
                CommentsCount = 0,
                IsLiked = false,
                ImageUrls = new List<string>(),
                OriginalPostId = rootPostId,
                OriginalPost = originalPostDto,
                RepostsCount = 0,
                IsReposted = false
            };

            return Ok(new RepostResponseDto
            {
                IsReposted = true,
                RepostsCount = newRepostsCount,
                Post = repostDto
            });
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<PostResponseDto>> UpdatePost(int id, PostUpdateDto postDto)
        {
            var post = await _dbContext.Posts
                .Include(p => p.User)
                .Include(p => p.Images)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                _logger.LogWarning("User {UserId} attempted to update post {PostId} owned by another user.", userId, post.UserId);
                return Forbid();
            }

            var trimmedContent = postDto.Content?.Trim() ?? string.Empty;
            var finalImages = postDto.ImageUrls != null
                ? postDto.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)).ToList()
                : post.Images.Select(i => i.Url).ToList();
            var hasImages = finalImages.Count > 0;

            if (string.IsNullOrWhiteSpace(trimmedContent) && !hasImages)
            {
                return BadRequest("Post must contain text content or at least one image.");
            }

            post.Content = trimmedContent;
            if (postDto.Visibility.HasValue)
            {
                post.Visibility = postDto.Visibility.Value;
            }

            if (postDto.ImageUrls != null)
            {
                _dbContext.PostImages.RemoveRange(post.Images);
                post.Images.Clear();

                var order = 0;
                foreach (var url in postDto.ImageUrls.Where(u => !string.IsNullOrWhiteSpace(u)))
                {
                    post.Images.Add(new PostImage
                    {
                        PostId = post.Id,
                        Url = url.Trim(),
                        Order = order++,
                        CreatedAt = DateTime.UtcNow
                    });
                }
            }

            post.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Invalidate post cache
            await _cache.RemoveByTagAsync($"post-{id}");

            _logger.LogInformation("Post {PostId} updated successfully by user {UserId}", post.Id, userId);

            var likesCount = await _dbContext.Likes.CountAsync(l => l.PostId == id);
            var commentsCount = await _dbContext.Comments.CountAsync(c => c.PostId == id);
            var isLiked = await _dbContext.Likes.AnyAsync(l => l.PostId == id && l.UserId == userId);

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = post.User.Name,
                UserAvatarUrl = post.User.AvatarUrl,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = likesCount,
                CommentsCount = commentsCount,
                IsLiked = isLiked,
                ImageUrls = post.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList()
            };

            return Ok(responseDto);
        }

        [HttpDelete("{id:int}")]
        public async Task<ActionResult> DeletePost(int id)
        {
            var post = await _dbContext.Posts.FindAsync(id);
            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                return Forbid();
            }

            _dbContext.Posts.Remove(post);
            await _dbContext.SaveChangesAsync();

            // Invalidate both post cache and user profile cache (posts count changed)
            await _cache.RemoveByTagAsync($"post-{id}");
            if (post.OriginalPostId.HasValue)
            {
                await _cache.RemoveByTagAsync($"post-{post.OriginalPostId.Value}");
            }
            await _cache.RemoveByTagAsync($"user-{userId}");

            return NoContent();
        }
    }
}

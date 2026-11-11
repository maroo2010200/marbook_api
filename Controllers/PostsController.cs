using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Extensions;
using MarbookApi.Hubs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.AspNetCore.SignalR;
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
        INotificationService notificationService,
        IHubContext<NotificationHub> hubContext) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly ILogger _logger = logger;
        private readonly HybridCache _cache = cache;
        private readonly INotificationService _notificationService = notificationService;
        private readonly IHubContext<NotificationHub> _hubContext = hubContext;

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

            var followedUserIds = new List<int>();
            if (currentUserId.HasValue)
            {
                followedUserIds = await _dbContext.Follows
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

            if (userId.HasValue)
            {
                query = query.OrderByDescending(p => p.IsPinned).ThenByDescending(p => p.CreatedAt);
            }
            else
            {
                query = query.OrderByDescending(p => p.CreatedAt);
            }

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
                        ImageUrls = p.OriginalPost.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                        IsPinned = p.OriginalPost.IsPinned
                    },
                    RepostsCount = p.Reposts.Count(),
                    IsReposted = currentUserId.HasValue && p.Reposts.Any(r => r.UserId == currentUserId.Value),
                    IsPinned = p.IsPinned
                })
                .ToListAsync();

            var followedSet = followedUserIds.ToHashSet();
            foreach (var post in posts)
            {
                post.OriginalPost = RedactOriginalPostIfRestricted(post.OriginalPost, currentUserId, followedSet);
            }

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

            var followedUserIds = new List<int>();
            if (currentUserId.HasValue)
            {
                followedUserIds = await _dbContext.Follows
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

            var followedSet = followedUserIds.ToHashSet();
            foreach (var post in posts)
            {
                post.OriginalPost = RedactOriginalPostIfRestricted(post.OriginalPost, currentUserId, followedSet);
            }

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
                        p.Reposts.Count,
                        p.IsPinned
                    );
                },
                tags: [$"post-{id}"],
                cancellationToken: HttpContext.RequestAborted);

            if (post == null)
            {
                return NotFound();
            }

            if (!await CanViewPostAsync(post.Visibility, post.UserId, currentUserId))
            {
                return NotFound();
            }

            var visibleOriginalPost = post.OriginalPost;
            if (visibleOriginalPost != null && !await CanViewPostAsync(visibleOriginalPost.Visibility, visibleOriginalPost.UserId, currentUserId))
            {
                visibleOriginalPost = null;
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
                OriginalPost = visibleOriginalPost,
                RepostsCount = post.RepostsCount,
                IsReposted = isReposted,
                IsPinned = post.IsPinned
            };

            return Ok(responseDto);
        }

        [HttpPost]
        [EnableRateLimiting("post-policy")]
        public async Task<ActionResult<PostResponseDto>> CreatePost(PostCreateDto postDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

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

                if (!await CanViewPostAsync(targetPost.Visibility, targetPost.UserId, userId))
                {
                    return Forbid();
                }

                rootPostId = targetPost.OriginalPostId ?? targetPost.Id;
                originalPost = targetPost.OriginalPostId.HasValue && targetPost.OriginalPost != null
                    ? targetPost.OriginalPost
                    : targetPost;

                if (originalPost.Visibility != PostVisibility.Public)
                {
                    return BadRequest("Only public posts can be reposted.");
                }
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

            _logger.LogUserAction(
                rootPostId.HasValue ? "PostReposted" : "PostCreated",
                userId,
                user.Username,
                "Post",
                post.Id,
                rootPostId.HasValue ? $"OriginalPostId={rootPostId.Value},Visibility={post.Visibility}" : $"Visibility={post.Visibility}");

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
                IsReposted = false,
                IsPinned = post.IsPinned
            };

            await BroadcastPostCreatedAsync(responseDto);

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

            var rootPostId = targetPost.OriginalPostId ?? targetPost.Id;
            var originalPost = targetPost.OriginalPostId.HasValue && targetPost.OriginalPost != null
                ? targetPost.OriginalPost
                : targetPost;

            var existingRepost = await _dbContext.Posts
                .FirstOrDefaultAsync(p => p.UserId == userId && p.OriginalPostId == rootPostId);

            if (existingRepost != null)
            {
                // Always allow un-reposting even if the original post was later restricted
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

                await _hubContext.Clients.All.SendAsync("PostDeleted", new { postId = existingRepost.Id });

                _logger.LogUserAction("PostUnreposted", userId, User.GetUsernameOrDefault(), "Post", rootPostId);

                return Ok(new RepostResponseDto
                {
                    IsReposted = false,
                    RepostsCount = count,
                    Post = null
                });
            }

            if (!await CanViewPostAsync(targetPost.Visibility, targetPost.UserId, userId))
            {
                return Forbid();
            }

            if (originalPost.Visibility != PostVisibility.Public)
            {
                return BadRequest("Only public posts can be reposted.");
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
                IsReposted = false,
                IsPinned = repost.IsPinned
            };

            await BroadcastPostCreatedAsync(repostDto);

            _logger.LogUserAction("PostReposted", userId, user.Username, "Post", repost.Id, $"OriginalPostId={rootPostId}");

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
                _logger.LogSecurityWarning("PostUpdateDenied", userId, User.GetUsernameOrDefault(), "Post", id, $"OwnerUserId={post.UserId}");
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

            // Invalidate post cache and any repost caches referencing this post
            await InvalidatePostAndRepostsCacheAsync(id);

            _logger.LogUserAction("PostUpdated", userId, post.User.Username, "Post", post.Id);

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
                ImageUrls = post.Images.OrderBy(i => i.Order).Select(i => i.Url).ToList(),
                IsPinned = post.IsPinned
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
                _logger.LogSecurityWarning("PostDeleteDenied", userId, User.GetUsernameOrDefault(), "Post", id, $"OwnerUserId={post.UserId}");
                return Forbid();
            }

            var repostIds = await _dbContext.Posts
                .Where(p => p.OriginalPostId == id)
                .Select(p => p.Id)
                .ToListAsync();

            _dbContext.Posts.Remove(post);
            await _dbContext.SaveChangesAsync();

            // Invalidate post cache, any repost caches, and user profile cache (posts count changed)
            await _cache.RemoveByTagAsync($"post-{id}");
            foreach (var repostId in repostIds)
            {
                await _cache.RemoveByTagAsync($"post-{repostId}");
            }
            if (post.OriginalPostId.HasValue)
            {
                await _cache.RemoveByTagAsync($"post-{post.OriginalPostId.Value}");
            }
            await _cache.RemoveByTagAsync($"user-{userId}");

            await _hubContext.Clients.All.SendAsync("PostDeleted", new { postId = id });

            _logger.LogUserAction("PostDeleted", userId, User.GetUsernameOrDefault(), "Post", id);

            return NoContent();
        }

        [HttpPost("{id:int}/Pin")]
        public async Task<IActionResult> TogglePinPost(int id)
        {
            var post = await _dbContext.Posts.FindAsync(id);
            if (post == null) return NotFound("Post not found.");

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                _logger.LogSecurityWarning("PostPinDenied", userId, User.GetUsernameOrDefault(), "Post", id, $"OwnerUserId={post.UserId}");
                return Forbid();
            }

            post.IsPinned = !post.IsPinned;

            if (post.IsPinned)
            {
                // Unpin previous pinned posts by this user so user has at most 1 pinned post
                var otherPinned = await _dbContext.Posts
                    .Where(p => p.UserId == userId && p.Id != id && p.IsPinned)
                    .ToListAsync();
                foreach (var op in otherPinned)
                {
                    op.IsPinned = false;
                }
            }

            await _dbContext.SaveChangesAsync();
            await _cache.RemoveByTagAsync($"post-{id}");
            await _cache.RemoveByTagAsync("posts");

            await _hubContext.Clients.All.SendAsync("PostPinned", new { postId = id, userId, isPinned = post.IsPinned });
            _logger.LogUserAction(post.IsPinned ? "PostPinned" : "PostUnpinned", userId, User.GetUsernameOrDefault(), "Post", id);
            return Ok(new { postId = id, isPinned = post.IsPinned });
        }

        private async Task<bool> CanViewPostAsync(PostVisibility visibility, int authorUserId, int? currentUserId)
        {
            if (visibility == PostVisibility.Public)
            {
                return true;
            }

            if (!currentUserId.HasValue)
            {
                return false;
            }

            if (authorUserId == currentUserId.Value)
            {
                return true;
            }

            if (visibility == PostVisibility.Followers)
            {
                return await _dbContext.Follows
                    .AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == authorUserId);
            }

            return false;
        }

        private static PostResponseDto? RedactOriginalPostIfRestricted(
            PostResponseDto? originalPost,
            int? currentUserId,
            ISet<int> followedUserIds)
        {
            if (originalPost == null || originalPost.Visibility == PostVisibility.Public)
            {
                return originalPost;
            }

            if (!currentUserId.HasValue)
            {
                return null;
            }

            if (originalPost.UserId == currentUserId.Value)
            {
                return originalPost;
            }

            if (originalPost.Visibility == PostVisibility.Followers && followedUserIds.Contains(originalPost.UserId))
            {
                return originalPost;
            }

            return null;
        }

        private async Task BroadcastPostCreatedAsync(PostResponseDto dto)
        {
            if (dto.Visibility == PostVisibility.Public)
            {
                await _hubContext.Clients.All.SendAsync("PostCreated", dto);
                return;
            }

            if (dto.Visibility == PostVisibility.Followers)
            {
                var recipientIds = await _dbContext.Follows
                    .Where(f => f.FolloweeId == dto.UserId)
                    .Select(f => f.FollowerId.ToString())
                    .ToListAsync();
                recipientIds.Add(dto.UserId.ToString());

                await _hubContext.Clients.Users(recipientIds).SendAsync("PostCreated", dto);
                return;
            }

            await _hubContext.Clients.User(dto.UserId.ToString()).SendAsync("PostCreated", dto);
        }

        private async Task InvalidatePostAndRepostsCacheAsync(int postId)
        {
            await _cache.RemoveByTagAsync($"post-{postId}");

            var repostIds = await _dbContext.Posts
                .Where(p => p.OriginalPostId == postId)
                .Select(p => p.Id)
                .ToListAsync();

            foreach (var repostId in repostIds)
            {
                await _cache.RemoveByTagAsync($"post-{repostId}");
            }
        }
    }
}

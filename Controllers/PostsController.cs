using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    [Authorize]
    public class PostsController(AppDbContext dbContext, ILogger<PostsController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly ILogger _logger = logger;

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

            var query = _dbContext.Posts.Include(p => p.User).AsQueryable();

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
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId,
                    LikesCount = p.Likes.Count(),
                    CommentsCount = p.Comments.Count(),
                    IsLiked = currentUserId.HasValue && p.Likes.Any(l => l.UserId == currentUserId.Value)
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
                .Where(p => EF.Functions.ToTsVector("english", p.Content)
                    .Matches(EF.Functions.WebSearchToTsQuery("english", search)));

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
                    CreatedAt = p.CreatedAt,
                    UpdatedAt = p.UpdatedAt,
                    UserId = p.UserId,
                    RelevanceScore = EF.Functions.ToTsVector("english", p.Content)
                        .Rank(EF.Functions.WebSearchToTsQuery("english", search))
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

            var post = await _dbContext.Posts
                .Include(p => p.User)
                .Include(p => p.Likes)
                .Include(p => p.Comments)
                .FirstOrDefaultAsync(p => p.Id == id);

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

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = post.User.Name,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = post.Likes.Count,
                CommentsCount = post.Comments.Count,
                IsLiked = currentUserId.HasValue && post.Likes.Any(l => l.UserId == currentUserId.Value)
            };

            return Ok(responseDto);
        }

        [HttpPost]
        public async Task<ActionResult<PostResponseDto>> CreatePost(PostCreateDto postDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            _logger.LogInformation("User {UserId} is creating a post with visibility {Visibility}.", userId, postDto.Visibility);

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                _logger.LogWarning(
                    "User {UserId} not found when creating a post.",
                    userId);

                return NotFound($"User with ID {userId} not found.");
            }

            var post = new Post
            {
                Content = postDto.Content,
                Visibility = postDto.Visibility,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Posts.Add(post);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Post {PostId} created successfully by user {UserId}.",
                post.Id,
                userId);

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = user.Name,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = 0,
                CommentsCount = 0,
                IsLiked = false
            };

            return CreatedAtAction(nameof(GetPost), new { id = post.Id }, responseDto);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<PostResponseDto>> UpdatePost(int id, PostUpdateDto postDto)
        {
            var post = await _dbContext.Posts
                .Include(p => p.User)
                .FirstOrDefaultAsync(p => p.Id == id);

            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (post.UserId != userId)
            {
                _logger.LogWarning(
                    "User {UserId} attempted to update post {PostId} owned by another user.",
                    userId,
                    post.UserId);

                return Forbid();
            }

            post.Content = postDto.Content;
            if (postDto.Visibility.HasValue)
            {
                post.Visibility = postDto.Visibility.Value;
            }
            post.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            _logger.LogInformation(
                "Post {PostId} updated successfully by user {UserId}",
                post.Id,
                userId);

            var likesCount = await _dbContext.Likes.CountAsync(l => l.PostId == id);
            var commentsCount = await _dbContext.Comments.CountAsync(c => c.PostId == id);
            var isLiked = await _dbContext.Likes.AnyAsync(l => l.PostId == id && l.UserId == userId);

            var responseDto = new PostResponseDto
            {
                Id = post.Id,
                Content = post.Content,
                Visibility = post.Visibility,
                UserName = post.User.Name,
                CreatedAt = post.CreatedAt,
                UpdatedAt = post.UpdatedAt,
                UserId = post.UserId,
                LikesCount = likesCount,
                CommentsCount = commentsCount,
                IsLiked = isLiked
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

            return NoContent();
        }
    }
}

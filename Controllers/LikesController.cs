using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Extensions;
using MarbookApi.Hubs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.SignalR;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Caching.Hybrid;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/posts/{postId:int}/[controller]")]
    [Authorize]
    public class LikesController(
        AppDbContext dbContext,
        INotificationService notificationService,
        HybridCache cache,
        IHubContext<NotificationHub> hubContext,
        ILogger<LikesController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly INotificationService _notificationService = notificationService;
        private readonly HybridCache _cache = cache;
        private readonly IHubContext<NotificationHub> _hubContext = hubContext;
        private readonly ILogger _logger = logger;

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<UserSearchDto>>> GetPostLikes(int postId, [FromQuery] PaginationParams pagination)
        {
            var post = await _dbContext.Posts
                .AsNoTracking()
                .Where(p => p.Id == postId)
                .Select(p => new { p.Id, p.Visibility, p.UserId })
                .FirstOrDefaultAsync();

            if (post == null)
            {
                return NotFound("Post not found.");
            }

            var currentUserId = User.GetUserIdOrNull();
            if (!await _dbContext.CanUserViewPostAsync(post.Visibility, post.UserId, currentUserId))
            {
                return NotFound("Post not found.");
            }

            var query = _dbContext.Likes
                .AsNoTracking()
                .Where(l => l.PostId == postId)
                .OrderByDescending(l => l.CreatedAt)
                .Select(l => new UserSearchDto
                {
                    Id = l.User.Id,
                    Username = l.User.Username,
                    Name = l.User.Name,
                    AvatarUrl = l.User.AvatarUrl,
                    IsMe = currentUserId.HasValue && l.UserId == currentUserId.Value
                });

            var result = await query.ToPagedFollowAwareResultAsync(_dbContext, currentUserId, pagination);
            return Ok(result);
        }

        [HttpPost]
        public async Task<ActionResult<Like>> CreateLike(int postId)
        {
            var post = await _dbContext.Posts.FindAsync(postId);
            if (post == null)
            {
                return NotFound("Post not found.");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return BadRequest("Invalid UserId.");
            }

            var existingLike = await _dbContext.Likes
                .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId);
            if (existingLike != null)
            {
                return Conflict("User has already liked this post.");
            }

            var like = new Like
            {
                PostId = postId,
                UserId = userId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Likes.Add(like);
            await _dbContext.SaveChangesAsync();

            // Invalidate post cache so likes count updates
            await _cache.RemoveByTagAsync($"post-{postId}");

            await _notificationService.CreateNotificationAsync(
                post.UserId,
                userId,
                NotificationType.PostLiked,
                postId);

            var likeCount = await _dbContext.Likes.CountAsync(l => l.PostId == postId);
            await _hubContext.Clients.All.SendAsync("PostLiked", new { postId, likeCount, userId, isLiked = true });
            _logger.LogUserAction("PostLiked", userId, user.Username, "Post", postId);
            return Ok(new { liked = true, likeCount });
        }

        [HttpDelete]
        public async Task<IActionResult> DeleteLikeForPost(int postId)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var like = await _dbContext.Likes
                .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == userId);
            if (like == null)
            {
                return NotFound("Like not found.");
            }

            var post = await _dbContext.Posts.FindAsync(postId);

            _dbContext.Likes.Remove(like);
            await _dbContext.SaveChangesAsync();

            // Invalidate post cache so likes count updates
            await _cache.RemoveByTagAsync($"post-{postId}");

            if (post != null)
            {
                await _notificationService.DeleteNotificationAsync(
                    post.UserId,
                    userId,
                    NotificationType.PostLiked,
                    postId);
            }

            var likeCount = await _dbContext.Likes.CountAsync(l => l.PostId == postId);
            await _hubContext.Clients.All.SendAsync("PostLiked", new { postId, likeCount, userId, isLiked = false });
            _logger.LogUserAction("PostUnliked", userId, User.GetUsernameOrDefault(), "Post", postId);
            return Ok(new { liked = false, likeCount });
        }
    }
}

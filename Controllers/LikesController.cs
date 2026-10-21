using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/posts/{postId:int}/[controller]")]
    [Authorize]
    public class LikesController(AppDbContext dbContext, INotificationService notificationService) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly INotificationService _notificationService = notificationService;

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

            await _notificationService.CreateNotificationAsync(
                post.UserId,
                userId,
                NotificationType.PostLiked,
                postId);

            var likeCount = await _dbContext.Likes.CountAsync(l => l.PostId == postId);
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

            if (post != null)
            {
                await _notificationService.DeleteNotificationAsync(
                    post.UserId,
                    userId,
                    NotificationType.PostLiked,
                    postId);
            }

            var likeCount = await _dbContext.Likes.CountAsync(l => l.PostId == postId);
            return Ok(new { liked = false, likeCount });
        }
    }
}

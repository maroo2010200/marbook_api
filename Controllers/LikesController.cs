using MarbookApi.Data;
using Microsoft.AspNetCore.Mvc;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/posts/{postId:int}/[controller]")]
    [Authorize]
    public class LikesController(AppDbContext dbContext) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;

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

            return Created();
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteLike(int postId, int id)
        {
            var like = await _dbContext.Likes.FindAsync(id);
            if (like == null)
            {
                return NotFound();
            }

            _dbContext.Likes.Remove(like);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}
using MarbookApi.Data;
using Microsoft.AspNetCore.Mvc;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/posts/{postId:int}/[controller]")]
    public class LikesController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public LikesController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpPost]
        public async Task<ActionResult<Like>> CreateLike(int postId, LikeCreateDto likeCreateDto)
        {
            var post = await _dbContext.Posts.FindAsync(postId);
            if (post == null)
            {
                return NotFound("Post not found.");
            }

            var user = await _dbContext.Users.FindAsync(likeCreateDto.UserId);
            if (user == null)
            {
                return BadRequest("Invalid UserId.");
            }

            var existingLike = await _dbContext.Likes
                .FirstOrDefaultAsync(l => l.PostId == postId && l.UserId == likeCreateDto.UserId);
            if (existingLike != null)
            {
                return Conflict("User has already liked this post.");
            }

            var like = new Like
            {
                PostId = postId,
                UserId = likeCreateDto.UserId,
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
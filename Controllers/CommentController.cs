using MarbookApi.Data;
using Microsoft.AspNetCore.Mvc;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/posts/{postId:int}/[controller]")]
    public class CommentsController : ControllerBase
    {
        private readonly AppDbContext _dbContext;

        public CommentsController(AppDbContext dbContext)
        {
            _dbContext = dbContext;
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<CommentResponseDto>>> GetComments(int postId)
        {
            var comments = await _dbContext.Comments
                .Where(c => c.PostId == postId)
                .OrderByDescending(c => c.CreatedAt)
                .Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    UserName = c.User.Name,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    UserId = c.UserId
                })
                .ToListAsync();

            return Ok(comments);
        }

        [HttpPost]
        public async Task<ActionResult<CommentResponseDto>> CreateComment(int postId, CommentCreateDto commentCreateDto)
        {
            var post = await _dbContext.Posts.FindAsync(postId);
            if (post == null)
            {
                return NotFound();
            }

            var user = await _dbContext.Users.FindAsync(commentCreateDto.UserId);
            if (user == null)
            {
                return BadRequest("Invalid UserId.");
            }

            var comment = new Comment
            {
                Content = commentCreateDto.Content,
                UserId = commentCreateDto.UserId,
                PostId = postId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Comments.Add(comment);
            await _dbContext.SaveChangesAsync();

            var commentResponseDto = new CommentResponseDto
            {
                Id = comment.Id,
                Content = comment.Content,
                UserName = user.Name,
                CreatedAt = comment.CreatedAt,
                UpdatedAt = comment.UpdatedAt,
                UserId = comment.UserId
            };

            return CreatedAtAction(nameof(GetComments), new { postId }, commentResponseDto);
        }

        [HttpPut("{commentId:int}")]
        public async Task<IActionResult> UpdateComment(int postId, int commentId, CommentUpdateDto commentUpdateDto)
        {
            var comment = await _dbContext.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.PostId == postId);
            if (comment == null)
            {
                return NotFound();
            }

            comment.Content = commentUpdateDto.Content;
            comment.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        [HttpDelete("{commentId:int}")]
        public async Task<IActionResult> DeleteComment(int postId, int commentId)
        {
            var comment = await _dbContext.Comments.FirstOrDefaultAsync(c => c.Id == commentId && c.PostId == postId);
            if (comment == null)
            {
                return NotFound();
            }

            _dbContext.Comments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}
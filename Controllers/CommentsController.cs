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
    public class CommentsController(AppDbContext dbContext) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;

        [HttpGet]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<CommentResponseDto>>> GetComments(int postId, [FromQuery] PaginationParams pagination)
        {
            var query = _dbContext.Comments.Where(c => c.PostId == postId).OrderByDescending(c => c.CreatedAt);

            var totalCount = await query.CountAsync();
            
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var comments = await query
            .Skip(skip)
            .Take(pagination.PageSize)
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

            return Ok(new PagedResult<CommentResponseDto>
            {
                Items = comments,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpPost]
        public async Task<ActionResult<CommentResponseDto>> CreateComment(int postId, CommentCreateDto commentCreateDto)
        {
            var post = await _dbContext.Posts.FindAsync(postId);
            if (post == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return BadRequest("Invalid UserId.");
            }

            var comment = new Comment
            {
                Content = commentCreateDto.Content,
                UserId = userId,
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

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if(comment.UserId != userId)
            {
                return Forbid();
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

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if(comment.UserId != userId)
            {
                return Forbid();
            }

            _dbContext.Comments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }
    }
}
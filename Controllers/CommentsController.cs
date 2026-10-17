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
            int? currentUserId = User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var query = _dbContext.Comments
                .Where(c => c.PostId == postId && c.ParentCommentId == null)
                .OrderByDescending(c => c.CreatedAt);

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
                    UserId = c.UserId,
                    ParentCommentId = c.ParentCommentId,
                    LikeCount = c.Likes.Count(),
                    ReplyCount = 0,
                    Liked = currentUserId.HasValue && c.Likes.Any(l => l.UserId == currentUserId.Value)
                })
                .ToListAsync();

            if (comments.Count > 0)
            {
                var hierarchy = await _dbContext.Comments
                    .Where(c => c.PostId == postId && c.ParentCommentId != null)
                    .Select(c => new { c.Id, c.ParentCommentId })
                    .ToListAsync();

                var childrenMap = hierarchy
                    .GroupBy(c => c.ParentCommentId!.Value)
                    .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

                int CountAllDescendants(int id)
                {
                    if (!childrenMap.TryGetValue(id, out var childIds)) return 0;
                    int count = childIds.Count;
                    foreach (var childId in childIds)
                    {
                        count += CountAllDescendants(childId);
                    }
                    return count;
                }

                foreach (var comment in comments)
                {
                    comment.ReplyCount = CountAllDescendants(comment.Id);
                }
            }

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
                UserId = comment.UserId,
                ParentCommentId = null,
                LikeCount = 0,
                ReplyCount = 0,
                Liked = false
            };

            return CreatedAtAction(nameof(GetComments), new { postId }, commentResponseDto);
        }

        [HttpPost("/api/comments/{commentId:int}/Likes")]
        public async Task<IActionResult> ToggleCommentLike(int commentId)
        {
            var comment = await _dbContext.Comments.FindAsync(commentId);
            if (comment == null)
            {
                return NotFound("Comment not found.");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var existingLike = await _dbContext.CommentLikes
                .FirstOrDefaultAsync(cl => cl.CommentId == commentId && cl.UserId == userId);

            bool liked;
            if (existingLike != null)
            {
                _dbContext.CommentLikes.Remove(existingLike);
                liked = false;
            }
            else
            {
                _dbContext.CommentLikes.Add(new CommentLike
                {
                    CommentId = commentId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                });
                liked = true;
            }

            await _dbContext.SaveChangesAsync();

            var likeCount = await _dbContext.CommentLikes.CountAsync(cl => cl.CommentId == commentId);
            return Ok(new { liked, likeCount });
        }

        [HttpPost("/api/comments/{commentId:int}/Replies")]
        public async Task<ActionResult<CommentResponseDto>> CreateReply(int commentId, CommentCreateDto replyDto)
        {
            var parentComment = await _dbContext.Comments.FindAsync(commentId);
            if (parentComment == null)
            {
                return NotFound("Comment not found.");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return BadRequest("Invalid UserId.");
            }

            var reply = new Comment
            {
                Content = replyDto.Content,
                UserId = userId,
                PostId = parentComment.PostId,
                ParentCommentId = commentId,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Comments.Add(reply);
            await _dbContext.SaveChangesAsync();

            var response = new CommentResponseDto
            {
                Id = reply.Id,
                Content = reply.Content,
                UserName = user.Name,
                CreatedAt = reply.CreatedAt,
                UpdatedAt = reply.UpdatedAt,
                UserId = reply.UserId,
                ParentCommentId = commentId,
                LikeCount = 0,
                ReplyCount = 0,
                Liked = false
            };

            return Ok(response);
        }

        [HttpGet("/api/comments/{commentId:int}/Replies")]
        [AllowAnonymous]
        public async Task<ActionResult<List<CommentResponseDto>>> GetReplies(int commentId)
        {
            int? currentUserId = User.Identity?.IsAuthenticated == true && int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var replies = await _dbContext.Comments
                .Where(c => c.ParentCommentId == commentId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    UserName = c.User.Name,
                    CreatedAt = c.CreatedAt,
                    UpdatedAt = c.UpdatedAt,
                    UserId = c.UserId,
                    ParentCommentId = c.ParentCommentId,
                    LikeCount = c.Likes.Count(),
                    ReplyCount = 0,
                    Liked = currentUserId.HasValue && c.Likes.Any(l => l.UserId == currentUserId.Value)
                })
                .ToListAsync();

            if (replies.Count > 0)
            {
                var parentComment = await _dbContext.Comments
                    .Where(c => c.Id == commentId)
                    .Select(c => new { c.PostId })
                    .FirstOrDefaultAsync();

                if (parentComment != null)
                {
                    var hierarchy = await _dbContext.Comments
                        .Where(c => c.PostId == parentComment.PostId && c.ParentCommentId != null)
                        .Select(c => new { c.Id, c.ParentCommentId })
                        .ToListAsync();

                    var childrenMap = hierarchy
                        .GroupBy(c => c.ParentCommentId!.Value)
                        .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

                    int CountAllDescendants(int id)
                    {
                        if (!childrenMap.TryGetValue(id, out var childIds)) return 0;
                        int count = childIds.Count;
                        foreach (var childId in childIds)
                        {
                            count += CountAllDescendants(childId);
                        }
                        return count;
                    }

                    foreach (var reply in replies)
                    {
                        reply.ReplyCount = CountAllDescendants(reply.Id);
                    }
                }
            }

            return Ok(replies);
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

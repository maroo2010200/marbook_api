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
                .Include(c => c.User)
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
                    ImageUrl = c.ImageUrl,
                    UserName = c.User.Name,
                    UserAvatarUrl = c.User.AvatarUrl,
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
                var replyCounts = await GetReplyCountsForPostAsync(postId);
                foreach (var comment in comments)
                {
                    comment.ReplyCount = replyCounts.GetValueOrDefault(comment.Id, 0);
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
            if (string.IsNullOrWhiteSpace(commentCreateDto.Content) && string.IsNullOrWhiteSpace(commentCreateDto.ImageUrl))
            {
                return BadRequest("Comment must contain either text or an image/GIF.");
            }

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
                Content = commentCreateDto.Content?.Trim() ?? string.Empty,
                ImageUrl = string.IsNullOrWhiteSpace(commentCreateDto.ImageUrl) ? null : commentCreateDto.ImageUrl.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PostId = postId,
                UserId = userId,
                ParentCommentId = null
            };

            _dbContext.Comments.Add(comment);
            await _dbContext.SaveChangesAsync();

            var commentResponseDto = new CommentResponseDto
            {
                Id = comment.Id,
                Content = comment.Content,
                ImageUrl = comment.ImageUrl,
                UserName = user.Name,
                UserAvatarUrl = user.AvatarUrl,
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

            if (existingLike != null)
            {
                _dbContext.CommentLikes.Remove(existingLike);
            }
            else
            {
                var newLike = new CommentLike
                {
                    CommentId = commentId,
                    UserId = userId,
                    CreatedAt = DateTime.UtcNow,
                    UpdatedAt = DateTime.UtcNow
                };
                _dbContext.CommentLikes.Add(newLike);
            }

            await _dbContext.SaveChangesAsync();

            var likeCount = await _dbContext.CommentLikes.CountAsync(cl => cl.CommentId == commentId);
            var isLiked = existingLike == null;

            return Ok(new { likeCount, isLiked });
        }

        [HttpPost("/api/comments/{commentId:int}/Replies")]
        public async Task<ActionResult<CommentResponseDto>> CreateReply(int commentId, CommentCreateDto replyDto)
        {
            if (string.IsNullOrWhiteSpace(replyDto.Content) && string.IsNullOrWhiteSpace(replyDto.ImageUrl))
            {
                return BadRequest("Reply must contain either text or an image/GIF.");
            }

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
                Content = replyDto.Content?.Trim() ?? string.Empty,
                ImageUrl = string.IsNullOrWhiteSpace(replyDto.ImageUrl) ? null : replyDto.ImageUrl.Trim(),
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow,
                PostId = parentComment.PostId,
                UserId = userId,
                ParentCommentId = commentId
            };

            _dbContext.Comments.Add(reply);
            await _dbContext.SaveChangesAsync();

            var response = new CommentResponseDto
            {
                Id = reply.Id,
                Content = reply.Content,
                ImageUrl = reply.ImageUrl,
                UserName = user.Name,
                UserAvatarUrl = user.AvatarUrl,
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
                .Include(c => c.User)
                .Where(c => c.ParentCommentId == commentId)
                .OrderBy(c => c.CreatedAt)
                .Select(c => new CommentResponseDto
                {
                    Id = c.Id,
                    Content = c.Content,
                    ImageUrl = c.ImageUrl,
                    UserName = c.User.Name,
                    UserAvatarUrl = c.User.AvatarUrl,
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
                    var replyCounts = await GetReplyCountsForPostAsync(parentComment.PostId);
                    foreach (var reply in replies)
                    {
                        reply.ReplyCount = replyCounts.GetValueOrDefault(reply.Id, 0);
                    }
                }
            }

            return Ok(replies);
        }

        [HttpPut("/api/comments/{commentId:int}")]
        public async Task<ActionResult<CommentResponseDto>> UpdateComment(
            int commentId,
            CommentUpdateDto commentUpdateDto)
        {
            if (string.IsNullOrWhiteSpace(commentUpdateDto.Content) && string.IsNullOrWhiteSpace(commentUpdateDto.ImageUrl))
            {
                return BadRequest("Comment must contain either text or an image/GIF.");
            }

            var query = _dbContext.Comments.Include(c => c.User).AsQueryable();
            
            var comment = await query.FirstOrDefaultAsync(c => c.Id == commentId);
            if (comment == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (comment.UserId != userId)
            {
                return Forbid();
            }

            comment.Content = commentUpdateDto.Content?.Trim() ?? string.Empty;
            if (commentUpdateDto.ImageUrl != null)
            {
                comment.ImageUrl = string.IsNullOrWhiteSpace(commentUpdateDto.ImageUrl) ? null : commentUpdateDto.ImageUrl.Trim();
            }
            comment.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            var likeCount = await _dbContext.CommentLikes.CountAsync(cl => cl.CommentId == commentId);
            var isLiked = await _dbContext.CommentLikes.AnyAsync(cl => cl.CommentId == commentId && cl.UserId == userId);

            var replyCounts = await GetReplyCountsForPostAsync(comment.PostId);

            var responseDto = new CommentResponseDto
            {
                Id = comment.Id,
                Content = comment.Content,
                ImageUrl = comment.ImageUrl,
                UserName = comment.User.Name,
                UserAvatarUrl = comment.User.AvatarUrl,
                CreatedAt = comment.CreatedAt,
                UpdatedAt = comment.UpdatedAt,
                UserId = comment.UserId,
                ParentCommentId = comment.ParentCommentId,
                LikeCount = likeCount,
                ReplyCount = replyCounts.GetValueOrDefault(comment.Id, 0),
                Liked = isLiked
            };

            return Ok(responseDto);
        }

        [HttpDelete("/api/comments/{commentId:int}")]
        public async Task<IActionResult> DeleteComment(int commentId)
        {
            var comment = await _dbContext.Comments.FindAsync(commentId);
            if (comment == null)
            {
                return NotFound();
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            if (comment.UserId != userId)
            {
                return Forbid();
            }

            _dbContext.Comments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            return NoContent();
        }

        private async Task<Dictionary<int, int>> GetReplyCountsForPostAsync(int postId)
        {
            var hierarchy = await _dbContext.Comments
                .Where(c => c.PostId == postId && c.ParentCommentId != null)
                .Select(c => new { c.Id, c.ParentCommentId })
                .ToListAsync();

            if (hierarchy.Count == 0)
            {
                return new Dictionary<int, int>();
            }

            var childrenMap = hierarchy
                .GroupBy(c => c.ParentCommentId!.Value)
                .ToDictionary(g => g.Key, g => g.Select(x => x.Id).ToList());

            int CountDescendants(int id)
            {
                if (!childrenMap.TryGetValue(id, out var childIds)) return 0;
                int count = childIds.Count;
                foreach (var childId in childIds)
                {
                    count += CountDescendants(childId);
                }
                return count;
            }

            var result = new Dictionary<int, int>();
            foreach (var node in hierarchy)
            {
                result[node.Id] = CountDescendants(node.Id);
            }
            foreach (var parentId in childrenMap.Keys)
            {
                result[parentId] = CountDescendants(parentId);
            }

            return result;
        }
    }
}

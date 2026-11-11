using System.Linq.Expressions;
﻿using System.Security.Claims;
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
    public class CommentsController(
        AppDbContext dbContext,
        INotificationService notificationService,
        HybridCache cache,
        IHubContext<NotificationHub> hubContext,
        ILogger<CommentsController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly INotificationService _notificationService = notificationService;
        private readonly HybridCache _cache = cache;
        private readonly IHubContext<NotificationHub> _hubContext = hubContext;
        private readonly ILogger _logger = logger;

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
                .OrderByDescending(c => c.IsPinned)
                .ThenByDescending(c => c.CreatedAt);

            var totalCount = await query.CountAsync();
            
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var comments = await query
                .Skip(skip)
                .Take(pagination.PageSize)
                .Select(ProjectToDto(currentUserId))
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

            // Invalidate post cache so comments count updates
            await _cache.RemoveByTagAsync($"post-{postId}");

            await _notificationService.CreateNotificationAsync(
                post.UserId,
                userId,
                NotificationType.CommentCreated,
                postId,
                comment.Id);

            var commentResponseDto = MapToDto(comment, user);

            await _hubContext.Clients.All.SendAsync("CommentCreated", new { postId, comment = commentResponseDto });

            _logger.LogUserAction("CommentCreated", userId, user.Username, "Comment", comment.Id, $"PostId={postId}");

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

            if (isLiked)
            {
                await _notificationService.CreateNotificationAsync(
                    comment.UserId,
                    userId,
                    NotificationType.CommentLiked,
                    comment.PostId,
                    commentId);
            }
            else
            {
                await _notificationService.DeleteNotificationAsync(
                    comment.UserId,
                    userId,
                    NotificationType.CommentLiked,
                    comment.PostId,
                    commentId);
            }

            await _hubContext.Clients.All.SendAsync("CommentLiked", new { commentId, postId = comment.PostId, likeCount, userId, isLiked });

            _logger.LogUserAction(isLiked ? "CommentLiked" : "CommentUnliked", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"PostId={comment.PostId}");

            return Ok(new { likeCount, liked = isLiked, isLiked });
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

            // Invalidate post cache so comments count updates
            await _cache.RemoveByTagAsync($"post-{parentComment.PostId}");

            await _notificationService.CreateNotificationAsync(
                parentComment.UserId,
                userId,
                NotificationType.ReplyCreated,
                parentComment.PostId,
                reply.Id);

            var response = MapToDto(reply, user);

            await _hubContext.Clients.All.SendAsync("CommentCreated", new { postId = parentComment.PostId, comment = response });

            _logger.LogUserAction("ReplyCreated", userId, user.Username, "Comment", reply.Id, $"PostId={parentComment.PostId},ParentCommentId={commentId}");

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
                .OrderByDescending(c => c.IsPinned)
                .ThenBy(c => c.CreatedAt)
                .Select(ProjectToDto(currentUserId))
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
                _logger.LogSecurityWarning("CommentUpdateDenied", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"OwnerUserId={comment.UserId}");
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

            var responseDto = MapToDto(comment, comment.User, likeCount, replyCounts.GetValueOrDefault(comment.Id, 0), isLiked);

            _logger.LogUserAction("CommentUpdated", userId, comment.User.Username, "Comment", commentId, $"PostId={comment.PostId}");

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
                _logger.LogSecurityWarning("CommentDeleteDenied", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"OwnerUserId={comment.UserId}");
                return Forbid();
            }

            _dbContext.Comments.Remove(comment);
            await _dbContext.SaveChangesAsync();

            // Invalidate post cache so comments count updates
            await _cache.RemoveByTagAsync($"post-{comment.PostId}");

            await _hubContext.Clients.All.SendAsync("CommentDeleted", new { commentId, postId = comment.PostId });

            _logger.LogUserAction("CommentDeleted", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"PostId={comment.PostId}");

            return NoContent();
        }

        [HttpPost("/api/comments/{commentId:int}/Pin")]
        public async Task<IActionResult> TogglePinComment(int commentId)
        {
            var comment = await _dbContext.Comments
                .Include(c => c.Post)
                .FirstOrDefaultAsync(c => c.Id == commentId);

            if (comment == null)
            {
                return NotFound("Comment not found.");
            }

            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            // Pinned by post author only
            if (comment.Post.UserId != userId)
            {
                _logger.LogSecurityWarning("CommentPinDenied", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"PostAuthorUserId={comment.Post.UserId}");
                return Forbid();
            }

            comment.IsPinned = !comment.IsPinned;

            if (comment.IsPinned)
            {
                // Unpin any other pinned comments on the same post
                var otherPinned = await _dbContext.Comments
                    .Where(c => c.PostId == comment.PostId && c.Id != commentId && c.IsPinned)
                    .ToListAsync();
                foreach (var o in otherPinned)
                {
                    o.IsPinned = false;
                }
            }

            await _dbContext.SaveChangesAsync();

            // Invalidate post cache
            await _cache.RemoveByTagAsync($"post-{comment.PostId}");

            await _hubContext.Clients.All.SendAsync("CommentPinned", new { commentId, postId = comment.PostId, isPinned = comment.IsPinned });

            _logger.LogUserAction(comment.IsPinned ? "CommentPinned" : "CommentUnpinned", userId, User.GetUsernameOrDefault(), "Comment", commentId, $"PostId={comment.PostId}");

            return Ok(new { commentId, isPinned = comment.IsPinned });
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
        private static Expression<Func<Comment, CommentResponseDto>> ProjectToDto(int? currentUserId) => c => new CommentResponseDto
        {
            Id = c.Id,
            Content = c.Content,
            ImageUrl = c.ImageUrl,
            UserName = c.User.Name,
            UserUsername = c.User.Username,
            UserAvatarUrl = c.User.AvatarUrl,
            CreatedAt = c.CreatedAt,
            UpdatedAt = c.UpdatedAt,
            UserId = c.UserId,
            ParentCommentId = c.ParentCommentId,
            LikeCount = c.Likes.Count(),
            ReplyCount = 0,
            Liked = currentUserId.HasValue && c.Likes.Any(l => l.UserId == currentUserId.Value),
            IsPinned = c.IsPinned
        };

        private static CommentResponseDto MapToDto(Comment comment, User user, int likeCount = 0, int replyCount = 0, bool isLiked = false) => new()
        {
            Id = comment.Id,
            Content = comment.Content,
            ImageUrl = comment.ImageUrl,
            UserName = user.Name,
            UserUsername = user.Username,
            UserAvatarUrl = user.AvatarUrl,
            CreatedAt = comment.CreatedAt,
            UpdatedAt = comment.UpdatedAt,
            UserId = comment.UserId,
            ParentCommentId = comment.ParentCommentId,
            LikeCount = likeCount,
            ReplyCount = replyCount,
            Liked = isLiked,
            IsPinned = comment.IsPinned
        };
    }
}

using System.Security.Claims;
using MarbookApi.Data;
using MarbookApi.DTOs;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[Authorize]
public class NotificationsController(AppDbContext dbContext) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;

    [HttpGet]
    public async Task<ActionResult<PagedResult<NotificationResponseDto>>> GetNotifications(
        [FromQuery] PaginationParams pagination,
        [FromQuery] bool? unreadOnly = false)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var query = _dbContext.Notifications
            .Include(n => n.ActorUser)
            .Where(n => n.RecipientUserId == userId);

        if (unreadOnly == true)
        {
            query = query.Where(n => !n.IsRead);
        }

        var totalCount = await query.CountAsync();
        var skip = (pagination.PageNumber - 1) * pagination.PageSize;

        var items = await query
            .OrderByDescending(n => n.CreatedAt)
            .Skip(skip)
            .Take(pagination.PageSize)
            .Select(n => new NotificationResponseDto
            {
                Id = n.Id,
                RecipientUserId = n.RecipientUserId,
                ActorUserId = n.ActorUserId,
                ActorName = n.ActorUser.Name,
                ActorAvatarUrl = n.ActorUser.AvatarUrl,
                Type = n.Type.ToString(),
                TargetPostId = n.TargetPostId,
                TargetCommentId = n.TargetCommentId,
                IsRead = n.IsRead,
                CreatedAt = n.CreatedAt
            })
            .ToListAsync();

        return Ok(new PagedResult<NotificationResponseDto>
        {
            Items = items,
            TotalCount = totalCount,
            PageNumber = pagination.PageNumber,
            PageSize = pagination.PageSize
        });
    }

    [HttpGet("unread-count")]
    public async Task<ActionResult<UnreadCountDto>> GetUnreadCount()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var count = await _dbContext.Notifications
            .CountAsync(n => n.RecipientUserId == userId && !n.IsRead);

        return Ok(new UnreadCountDto { UnreadCount = count });
    }

    [HttpPut("{id:int}/read")]
    public async Task<IActionResult> MarkAsRead(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId);

        if (notification == null)
        {
            return NotFound("Notification not found.");
        }

        if (!notification.IsRead)
        {
            notification.IsRead = true;
            await _dbContext.SaveChangesAsync();
        }

        return NoContent();
    }

    [HttpPut("read-all")]
    public async Task<IActionResult> MarkAllAsRead()
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        await _dbContext.Notifications
            .Where(n => n.RecipientUserId == userId && !n.IsRead)
            .ExecuteUpdateAsync(s => s.SetProperty(n => n.IsRead, true));

        return NoContent();
    }

    [HttpDelete("{id:int}")]
    public async Task<IActionResult> DeleteNotification(int id)
    {
        var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

        var notification = await _dbContext.Notifications
            .FirstOrDefaultAsync(n => n.Id == id && n.RecipientUserId == userId);

        if (notification == null)
        {
            return NotFound("Notification not found.");
        }

        _dbContext.Notifications.Remove(notification);
        await _dbContext.SaveChangesAsync();

        return NoContent();
    }
}

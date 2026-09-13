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
    [Route("api/users/{userId:int}")]
    [Authorize]
    public class FollowsController(AppDbContext dbContext, ILogger<FollowsController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly ILogger _logger = logger;

        [HttpPost("follow")]
        public async Task<IActionResult> Follow(int userId)
        {
            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdStr, out int followerId))
            {
                return Unauthorized("Invalid user token.");
            }

            if (followerId == userId)
            {
                return BadRequest("You cannot follow yourself.");
            }

            var followeeExists = await _dbContext.Users.AnyAsync(u => u.Id == userId);
            if (!followeeExists)
            {
                return NotFound("User to follow not found.");
            }

            var alreadyFollowing = await _dbContext.Follows
                .AnyAsync(f => f.FollowerId == followerId && f.FolloweeId == userId);

            if (alreadyFollowing)
            {
                return Conflict("You are already following this user.");
            }

            var follow = new Follow
            {
                FollowerId = followerId,
                FolloweeId = userId,
                CreatedAt = DateTime.UtcNow
            };

            _dbContext.Follows.Add(follow);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User {FollowerId} started following User {FolloweeId}", followerId, userId);

            return Created();
        }

        [HttpDelete("unfollow")]
        public async Task<IActionResult> Unfollow(int userId)
        {
            var currentUserIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (!int.TryParse(currentUserIdStr, out int followerId))
            {
                return Unauthorized("Invalid user token.");
            }

            var follow = await _dbContext.Follows
                .FirstOrDefaultAsync(f => f.FollowerId == followerId && f.FolloweeId == userId);

            if (follow == null)
            {
                return NotFound("You are not following this user.");
            }

            _dbContext.Follows.Remove(follow);
            await _dbContext.SaveChangesAsync();

            _logger.LogInformation("User {FollowerId} unfollowed User {FolloweeId}", followerId, userId);

            return NoContent();
        }

        [HttpGet("followers")]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<UserSearchDto>>> GetFollowers(int userId, [FromQuery] PaginationParams pagination)
        {
            var userExists = await _dbContext.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                return NotFound("User not found.");
            }

            var query = _dbContext.Follows
                .Where(f => f.FolloweeId == userId)
                .Select(f => f.Follower);

            var totalCount = await query.CountAsync();
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var followers = await query
                .OrderBy(u => u.Name)
                .Skip(skip)
                .Take(pagination.PageSize)
                .Select(u => new UserSearchDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Name = u.Name
                }).ToListAsync();

            return Ok(new PagedResult<UserSearchDto>
            {
                Items = followers,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpGet("following")]
        [AllowAnonymous]
        public async Task<ActionResult<PagedResult<UserSearchDto>>> GetFollowing(int userId, [FromQuery] PaginationParams pagination)
        {
            var userExists = await _dbContext.Users.AnyAsync(u => u.Id == userId);
            if (!userExists)
            {
                return NotFound("User not found.");
            }

            var query = _dbContext.Follows
                .Where(f => f.FollowerId == userId)
                .Select(f => f.Followee);

            var totalCount = await query.CountAsync();
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var following = await query
                .OrderBy(u => u.Name)
                .Skip(skip)
                .Take(pagination.PageSize)
                .Select(u => new UserSearchDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Name = u.Name
                }).ToListAsync();

            return Ok(new PagedResult<UserSearchDto>
            {
                Items = following,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }
    }
}

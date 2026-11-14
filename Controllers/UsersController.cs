using MarbookApi.Data;
using MarbookApi.Extensions;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;
using Microsoft.Extensions.Caching.Hybrid;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController(AppDbContext dbContext, HybridCache cache, ILogger<UsersController> logger) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;
        private readonly HybridCache _cache = cache;
        private readonly ILogger _logger = logger;

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserResponseDto>> GetCurrentUser()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var dto = await GetCachedUserResponseAsync(userId);
            if (dto == null)
            {
                return NotFound();
            }

            return Ok(dto);
        }

        [HttpPut("me")]
        [HttpPatch("me")]
        [Authorize]
        public async Task<ActionResult<UserProfileDto>> UpdateMyProfile([FromBody] UserProfileUpdateDto profileDto)
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);
            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            if (!string.IsNullOrWhiteSpace(profileDto.Username))
            {
                var trimmedUsername = profileDto.Username.Trim();
                var usernameTaken = await _dbContext.Users
                    .AnyAsync(u => u.Username != null && u.Username.ToLower() == trimmedUsername.ToLower() && u.Id != userId);

                if (usernameTaken)
                {
                    return BadRequest("Username is already taken.");
                }

                user.Username = trimmedUsername;
            }

            if (profileDto.Name != null)
            {
                var trimmedName = profileDto.Name.Trim();
                if (string.IsNullOrWhiteSpace(trimmedName))
                {
                    return BadRequest("Name cannot be empty.");
                }
                user.Name = trimmedName;
            }

            if (profileDto.Bio != null)
            {
                user.Bio = string.IsNullOrWhiteSpace(profileDto.Bio) ? null : profileDto.Bio.Trim();
            }

            if (profileDto.Birthdate.HasValue && profileDto.Birthdate.Value != default)
            {
                user.Birthdate = profileDto.Birthdate.Value;
            }

            if (profileDto.IsBirthdatePrivate.HasValue)
            {
                user.IsBirthdatePrivate = profileDto.IsBirthdatePrivate.Value;
            }

            if (profileDto.AvatarUrl != null)
            {
                user.AvatarUrl = string.IsNullOrWhiteSpace(profileDto.AvatarUrl) ? null : profileDto.AvatarUrl.Trim();
            }

            if (profileDto.CoverUrl != null)
            {
                user.CoverUrl = string.IsNullOrWhiteSpace(profileDto.CoverUrl) ? null : profileDto.CoverUrl.Trim();
            }

            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Invalidate all cached data for this user (profile, user DTO, etc.)
            await _cache.RemoveByTagAsync($"user-{userId}");

            _logger.LogUserAction("ProfileUpdated", user.Id, user.Username, "User", user.Id);

            var result = await BuildUserProfileDto(user, userId);
            return Ok(result);
        }

        [HttpGet("{id:int}/profile")]
        public async Task<ActionResult<UserProfileDto>> GetUserProfile(int id)
        {
            var profileData = await _cache.GetOrCreateAsync(
                $"user:profile:{id}",
                async cancel =>
                {
                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancel);
                    if (user == null)
                    {
                        return null;
                    }

                    var followersCount = await _dbContext.Follows.CountAsync(f => f.FolloweeId == user.Id, cancel);
                    var followingCount = await _dbContext.Follows.CountAsync(f => f.FollowerId == user.Id, cancel);
                    var postsCount = await _dbContext.Posts.CountAsync(p => p.UserId == user.Id, cancel);

                    return new CachedUserProfileData(
                        user.Id,
                        user.Name,
                        user.Username,
                        user.Bio,
                        user.AvatarUrl,
                        user.CoverUrl,
                        user.Birthdate,
                        user.IsBirthdatePrivate,
                        user.CreatedAt,
                        followersCount,
                        followingCount,
                        postsCount
                    );
                },
                tags: [$"user-{id}"],
                cancellationToken: HttpContext.RequestAborted);

            if (profileData == null)
            {
                return NotFound("User not found.");
            }

            return Ok(await BuildUserProfileDtoAsync(profileData, GetCurrentUserId()));
        }

        [HttpGet("username/{username}/profile")]
        public async Task<ActionResult<UserProfileDto>> GetUserProfileByUsername(string username)
        {
            var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Username != null && u.Username.ToLower() == username.ToLower());
            if (user == null)
            {
                return NotFound("User not found.");
            }

            return await GetUserProfile(user.Id);
        }

        [HttpGet]
        public async Task<ActionResult<IEnumerable<UserResponseDto>>> GetUsers()
        {
            int? currentUserId = GetCurrentUserId();

            var users = await _dbContext.Users.Select(u => new UserResponseDto
            {
                Id = u.Id,
                Name = u.Name,
                Username = u.Username,
                Email = null,
                Bio = u.Bio,
                AvatarUrl = u.AvatarUrl,
                CoverUrl = u.CoverUrl,
                IsEmailVerified = u.IsEmailVerified,
                Birthdate = (u.IsBirthdatePrivate && currentUserId != u.Id) ? null : u.Birthdate,
                IsBirthdatePrivate = u.IsBirthdatePrivate,
                CreatedAt = u.CreatedAt,
                UpdatedAt = u.UpdatedAt
            }).ToListAsync();

            return Ok(users);
        }

        [HttpGet("search")]
        public async Task<ActionResult<PagedResult<UserSearchDto>>> SearchUsers([FromQuery] PaginationParams pagination, [FromQuery] string? search)
        {
            if (string.IsNullOrWhiteSpace(search))
            {
                return BadRequest("Search content must be valid.");
            }

            var term = search.Trim();
            int? currentUserId = GetCurrentUserId();

            var query = _dbContext.Users
                .Where(u =>
                    (u.Username != null && (
                        EF.Functions.ILike(u.Username, $"%{term}%")
                        || EF.Functions.TrigramsSimilarity(u.Username, term) > 0.2
                    ))
                    || EF.Functions.ILike(u.Name, $"%{term}%")
                    || EF.Functions.TrigramsSimilarity(u.Name, term) > 0.2
                );

            var totalCount = await query.CountAsync();
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var users = await query
                .OrderByDescending(u => EF.Functions.ILike(u.Name, $"{term}%") || (u.Username != null && EF.Functions.ILike(u.Username, $"{term}%")))
                .ThenByDescending(u => EF.Functions.TrigramsSimilarity(u.Name, term))
                .ThenBy(u => u.Name)
                .Skip(skip)
                .Take(pagination.PageSize)
                .Select(u => new UserSearchDto
                {
                    Id = u.Id,
                    Username = u.Username,
                    Name = u.Name,
                    AvatarUrl = u.AvatarUrl,
                    Bio = u.Bio,
                    FollowersCount = u.Followers.Count,
                    IsFollowing = currentUserId.HasValue && u.Followers.Any(f => f.FollowerId == currentUserId.Value),
                    IsMe = currentUserId.HasValue && u.Id == currentUserId.Value
                })
                .ToListAsync();

            return Ok(new PagedResult<UserSearchDto>
            {
                Items = users,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserResponseDto>> GetUser(int id)
        {
            var response = await GetCachedUserResponseAsync(id);
            if (response == null)
            {
                return NotFound();
            }

            return Ok(response.ForViewer(GetCurrentUserId() == response.Id));
        }

        [HttpPost("change-password")]
        [Authorize]
        public async Task<IActionResult> ChangePassword([FromBody] ChangePasswordDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.CurrentPassword, user.PasswordHash))
            {
                _logger.LogSecurityWarning("PasswordChangeFailed", userId, user.Username, "User", userId, "Incorrect current password");
                return BadRequest(new { message = "Current password is incorrect." });
            }

            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();
            await _cache.RemoveByTagAsync($"user-{userId}");

            _logger.LogUserAction("PasswordChanged", userId, user.Username, "User", userId);

            return Ok(new { message = "Password changed successfully." });
        }

        [HttpPost("delete-account")]
        [Authorize]
        public async Task<IActionResult> DeleteAccount([FromBody] DeleteAccountDto dto)
        {
            var userIdStr = User.FindFirstValue(ClaimTypes.NameIdentifier);
            if (string.IsNullOrEmpty(userIdStr) || !int.TryParse(userIdStr, out var userId))
            {
                return Unauthorized();
            }

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            if (!BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
            {
                _logger.LogSecurityWarning("AccountDeletionFailed", userId, user.Username, "User", userId, "Incorrect password");
                return BadRequest(new { message = "Incorrect password. Account was not deleted." });
            }

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            await _cache.RemoveByTagAsync($"user-{userId}");

            _logger.LogUserAction("AccountDeleted", userId, user.Username, "User", userId);

            return Ok(new { message = "Account deleted successfully." });
        }

        private int? GetCurrentUserId()
        {
            return User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;
        }

        private Task<UserResponseDto?> GetCachedUserResponseAsync(int userId)
        {
            return _cache.GetOrCreateAsync(
                $"user:{userId}",
                async cancel =>
                {
                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancel);
                    return user == null ? null : UserResponseDto.FromUser(user, isOwner: true);
                },
                tags: [$"user-{userId}"],
                cancellationToken: HttpContext.RequestAborted).AsTask();
        }

        private async Task<UserProfileDto> BuildUserProfileDto(User user, int? currentUserId)
        {
            var followersCount = await _dbContext.Follows.CountAsync(f => f.FolloweeId == user.Id);
            var followingCount = await _dbContext.Follows.CountAsync(f => f.FollowerId == user.Id);
            var postsCount = await _dbContext.Posts.CountAsync(p => p.UserId == user.Id);

            var data = new CachedUserProfileData(
                user.Id,
                user.Name,
                user.Username,
                user.Bio,
                user.AvatarUrl,
                user.CoverUrl,
                user.Birthdate,
                user.IsBirthdatePrivate,
                user.CreatedAt,
                followersCount,
                followingCount,
                postsCount
            );

            return await BuildUserProfileDtoAsync(data, currentUserId);
        }

        private async Task<UserProfileDto> BuildUserProfileDtoAsync(CachedUserProfileData profileData, int? currentUserId)
        {
            var isFollowing = currentUserId.HasValue && currentUserId.Value != profileData.Id
                && await _dbContext.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == profileData.Id);

            var isMe = currentUserId.HasValue && currentUserId.Value == profileData.Id;

            return new UserProfileDto
            {
                Id = profileData.Id,
                Name = profileData.Name,
                Username = profileData.Username,
                Bio = profileData.Bio,
                AvatarUrl = profileData.AvatarUrl,
                CoverUrl = profileData.CoverUrl,
                Birthdate = (profileData.IsBirthdatePrivate && !isMe) ? null : profileData.Birthdate,
                IsBirthdatePrivate = profileData.IsBirthdatePrivate,
                CreatedAt = profileData.CreatedAt,
                FollowersCount = profileData.FollowersCount,
                FollowingCount = profileData.FollowingCount,
                PostsCount = profileData.PostsCount,
                IsFollowing = isFollowing,
                IsMe = isMe
            };
        }
    }
}

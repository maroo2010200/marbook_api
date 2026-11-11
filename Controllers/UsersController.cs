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

            var dto = await _cache.GetOrCreateAsync(
                $"user:{userId}",
                async cancel =>
                {
                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == userId, cancel);
                    if (user == null)
                    {
                        return null;
                    }

                    return new UserResponseDto
                    {
                        Id = user.Id,
                        Name = user.Name,
                        Username = user.Username,
                        Email = user.Email,
                        Bio = user.Bio,
                        AvatarUrl = user.AvatarUrl,
                        CoverUrl = user.CoverUrl,
                        IsEmailVerified = user.IsEmailVerified,
                        Birthdate = user.Birthdate,
                        IsBirthdatePrivate = user.IsBirthdatePrivate,
                        CreatedAt = user.CreatedAt,
                        UpdatedAt = user.UpdatedAt
                    };
                },
                tags: [$"user-{userId}"],
                cancellationToken: HttpContext.RequestAborted);

            if (dto == null)
            {
                return NotFound();
            }

            return Ok(dto);
        }

        [HttpPut("me")]
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

            user.Name = profileDto.Name.Trim();
            user.Bio = profileDto.Bio?.Trim();
            if (profileDto.Birthdate != default)
            {
                user.Birthdate = profileDto.Birthdate;
            }
            if (profileDto.IsBirthdatePrivate.HasValue)
            {
                user.IsBirthdatePrivate = profileDto.IsBirthdatePrivate.Value;
            }
            user.AvatarUrl = string.IsNullOrWhiteSpace(profileDto.AvatarUrl) ? null : profileDto.AvatarUrl.Trim();
            user.CoverUrl = string.IsNullOrWhiteSpace(profileDto.CoverUrl) ? null : profileDto.CoverUrl.Trim();
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
                        user.Email,
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

            int? currentUserId = User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var isFollowing = currentUserId.HasValue && currentUserId.Value != id
                && await _dbContext.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == id);

            var isMe = currentUserId.HasValue && currentUserId.Value == id;

            var profile = new UserProfileDto
            {
                Id = profileData.Id,
                Name = profileData.Name,
                Username = profileData.Username,
                Email = profileData.Email,
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

            return Ok(profile);
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
            var users = await _dbContext.Users.Select(u => new UserResponseDto
            {
                Id = u.Id,
                Name = u.Name,
                Username = u.Username,
                Email = u.Email,
                Bio = u.Bio,
                AvatarUrl = u.AvatarUrl,
                CoverUrl = u.CoverUrl,
                IsEmailVerified = u.IsEmailVerified,
                Birthdate = u.Birthdate,
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
            int? currentUserId = User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

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
            var response = await _cache.GetOrCreateAsync(
                $"user:{id}",
                async cancel =>
                {
                    var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Id == id, cancel);

                    if (user == null)
                    {
                        return null;
                    }

                    return new UserResponseDto
                    {
                        Id = user.Id,
                        Name = user.Name,
                        Username = user.Username,
                        Email = user.Email,
                        Bio = user.Bio,
                        AvatarUrl = user.AvatarUrl,
                        CoverUrl = user.CoverUrl,
                        IsEmailVerified = user.IsEmailVerified,
                        Birthdate = user.Birthdate,
                        IsBirthdatePrivate = user.IsBirthdatePrivate,
                        CreatedAt = user.CreatedAt,
                        UpdatedAt = user.UpdatedAt
                    };
                },
                tags: [$"user-{id}"],
                cancellationToken: HttpContext.RequestAborted);

            if (response == null)
            {
                return NotFound();
            }

            int? currentUserId = User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            if (response.IsBirthdatePrivate && currentUserId != response.Id)
            {
                return Ok(new UserResponseDto
                {
                    Id = response.Id,
                    Name = response.Name,
                    Username = response.Username,
                    Email = response.Email,
                    Bio = response.Bio,
                    AvatarUrl = response.AvatarUrl,
                    CoverUrl = response.CoverUrl,
                    IsEmailVerified = response.IsEmailVerified,
                    Birthdate = null,
                    IsBirthdatePrivate = true,
                    CreatedAt = response.CreatedAt,
                    UpdatedAt = response.UpdatedAt
                });
            }

            return Ok(response);
        }

        [HttpPost]
        public async Task<ActionResult<User>> CreateUser(UserCreateDto userDto)
        {
            var emailExist = await _dbContext.Users.AnyAsync(u => u.Email == userDto.Email);
            if (emailExist)
            {
                return BadRequest("Email already exists.");
            }

            var user = new User
            {
                Name = userDto.Name,
                Username = userDto.Username,
                Email = userDto.Email,
                Bio = null,
                PasswordHash = BCrypt.Net.BCrypt.HashPassword(userDto.Password),
                Birthdate = userDto.Birthdate,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            };

            _dbContext.Users.Add(user);
            await _dbContext.SaveChangesAsync();

            return CreatedAtAction(nameof(GetUser), new { id = user.Id }, user);
        }

        [HttpPut("{id:int}")]
        public async Task<ActionResult<User>> UpdateUser(int id, UserUpdateDto userDto)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            bool emailExist = await _dbContext.Users.AnyAsync(u => u.Email == userDto.Email && u.Id != id);
            if (emailExist)
            {
                return BadRequest("Email already exists.");
            }

            user.Name = userDto.Name;
            user.Email = userDto.Email;
            user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(userDto.Password);
            user.Birthdate = userDto.Birthdate;

            if (userDto.Username != null)
            {
                user.Username = userDto.Username;
            }
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            // Invalidate all cached data for this user
            await _cache.RemoveByTagAsync($"user-{id}");

            return Ok(user);
        }

        [HttpDelete("{id:int}")]
        public async Task<IActionResult> DeleteUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound();
            }

            _dbContext.Users.Remove(user);
            await _dbContext.SaveChangesAsync();

            // Invalidate all cached data for this user
            await _cache.RemoveByTagAsync($"user-{id}");

            return NoContent();
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

        private async Task<UserProfileDto> BuildUserProfileDto(User user, int? currentUserId)
        {
            var followersCount = await _dbContext.Follows.CountAsync(f => f.FolloweeId == user.Id);
            var followingCount = await _dbContext.Follows.CountAsync(f => f.FollowerId == user.Id);
            var postsCount = await _dbContext.Posts.CountAsync(p => p.UserId == user.Id);

            var isFollowing = currentUserId.HasValue && currentUserId.Value != user.Id
                && await _dbContext.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == user.Id);

            var isMe = currentUserId.HasValue && currentUserId.Value == user.Id;

            return new UserProfileDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                Bio = user.Bio,
                AvatarUrl = user.AvatarUrl,
                CoverUrl = user.CoverUrl,
                Birthdate = (user.IsBirthdatePrivate && !isMe) ? null : user.Birthdate,
                IsBirthdatePrivate = user.IsBirthdatePrivate,
                CreatedAt = user.CreatedAt,
                FollowersCount = followersCount,
                FollowingCount = followingCount,
                PostsCount = postsCount,
                IsFollowing = isFollowing,
                IsMe = isMe
            };
        }
    }
}

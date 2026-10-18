using MarbookApi.Data;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.AspNetCore.Authorization;
using System.Security.Claims;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UsersController(AppDbContext dbContext) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;

        [HttpGet("me")]
        [Authorize]
        public async Task<ActionResult<UserResponseDto>> GetCurrentUser()
        {
            var userId = int.Parse(User.FindFirstValue(ClaimTypes.NameIdentifier)!);

            var user = await _dbContext.Users.FindAsync(userId);
            if (user == null)
            {
                return NotFound();
            }

            var dto = new UserResponseDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                Bio = user.Bio,
                IsEmailVerified = user.IsEmailVerified,
                Birthdate = user.Birthdate,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            };

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
            user.Birthdate = profileDto.Birthdate;
            user.UpdatedAt = DateTime.UtcNow;

            await _dbContext.SaveChangesAsync();

            var result = await BuildUserProfileDto(user, userId);
            return Ok(result);
        }

        [HttpGet("{id:int}/profile")]
        public async Task<ActionResult<UserProfileDto>> GetUserProfile(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);
            if (user == null)
            {
                return NotFound("User not found.");
            }

            int? currentUserId = User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var profile = await BuildUserProfileDto(user, currentUserId);
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

            int? currentUserId = User.Identity?.IsAuthenticated == true &&
                int.TryParse(User.FindFirstValue(ClaimTypes.NameIdentifier), out var uid)
                ? uid
                : null;

            var profile = await BuildUserProfileDto(user, currentUserId);
            return Ok(profile);
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

            var query = _dbContext.Users
                .Where(u => (
                    u.Username != null && EF.Functions.ILike(u.Username, $"{search}%"))
                    || EF.Functions.ILike(u.Name, $"{search}%")
                );

            var totalCount = await query.CountAsync();
            var skip = (pagination.PageNumber - 1) * pagination.PageSize;

            var users = await query
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
                Items = users,
                TotalCount = totalCount,
                PageNumber = pagination.PageNumber,
                PageSize = pagination.PageSize
            });
        }

        [HttpGet("{id:int}")]
        public async Task<ActionResult<UserResponseDto>> GetUser(int id)
        {
            var user = await _dbContext.Users.FindAsync(id);

            if (user == null)
            {
                return NotFound();
            }

            var response = new UserResponseDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                Bio = user.Bio,
                IsEmailVerified = user.IsEmailVerified,
                Birthdate = user.Birthdate,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            };
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

            return NoContent();
        }

        private async Task<UserProfileDto> BuildUserProfileDto(User user, int? currentUserId)
        {
            var followersCount = await _dbContext.Follows.CountAsync(f => f.FolloweeId == user.Id);
            var followingCount = await _dbContext.Follows.CountAsync(f => f.FollowerId == user.Id);
            var postsCount = await _dbContext.Posts.CountAsync(p => p.UserId == user.Id);

            var isFollowing = currentUserId.HasValue && currentUserId.Value != user.Id
                && await _dbContext.Follows.AnyAsync(f => f.FollowerId == currentUserId.Value && f.FolloweeId == user.Id);

            return new UserProfileDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                Bio = user.Bio,
                Birthdate = user.Birthdate,
                CreatedAt = user.CreatedAt,
                FollowersCount = followersCount,
                FollowingCount = followingCount,
                PostsCount = postsCount,
                IsFollowing = isFollowing,
                IsMe = currentUserId.HasValue && currentUserId.Value == user.Id
            };
        }
    }
}

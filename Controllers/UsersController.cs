using MarbookApi.Data;
using MarbookApi.Models;
using MarbookApi.DTOs;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers
{
    [ApiController]
    [Route("api/[controller]")]
    public class UserController(AppDbContext dbContext) : ControllerBase
    {
        private readonly AppDbContext _dbContext = dbContext;

        [HttpGet]
        public async Task<ActionResult<IEnumerable<User>>> GetUsers()
        {
            var users = await _dbContext.Users.Select(u => new UserResponseDto
            {
                Id = u.Id,
                Name = u.Name,
                Username = u.Username,
                Email = u.Email,
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

    }
}
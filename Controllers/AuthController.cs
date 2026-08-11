
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext dbContext, IJwtService jwtService) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IJwtService _jwtService = jwtService;

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        var emailExist = await _dbContext.Users.AnyAsync(u => u.Email == dto.Email);

        if (emailExist)
        {
            return Conflict("Email is already registered.");
        }

        if (!string.IsNullOrWhiteSpace(dto.Username))
        {
            var usernameExist = await _dbContext.Users.AnyAsync(u => u.Username == dto.Username);

            if (usernameExist)
            {
                return Conflict("Username is already taken.");
            }
        }

        var user = new User
        {
            Name = dto.Name,
            Email = dto.Email,
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Birthdate = dto.Birthdate
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60)
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            return Unauthorized("Invalid email or password.");
        }

        var token = _jwtService.GenerateToken(user);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddMinutes(60)
        });
    }
}

using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext dbContext, IJwtService jwtService, ILogger<AuthController> logger) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IJwtService _jwtService = jwtService;
    private readonly ILogger _logger = logger;

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

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogInformation(
            "User {UserId} registered successfully.", 
            user.Id);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt
        });
    }

    [HttpPost("login")]
    public async Task<ActionResult<AuthResponseDto>> Login(LoginDto dto)
    {
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == dto.Email);

        if (user == null || !BCrypt.Net.BCrypt.Verify(dto.Password, user.PasswordHash))
        {
            _logger.LogWarning(
                "Failed login attempt for email {Email}", 
                dto.Email);

            return Unauthorized("Invalid email or password.");
        }

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogInformation(
            "User {UserId} logged in successfully.",
            user.Id);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt
        });
    }
}
using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
public class AuthController(AppDbContext dbContext, IJwtService jwtService, ILogger<AuthController> logger, IEmailVerificationService emailVerificationService) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IJwtService _jwtService = jwtService;
    private readonly ILogger _logger = logger;
    private readonly IEmailVerificationService _emailVerificationService = emailVerificationService;

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
            IsEmailVerified = false,
            Username = dto.Username,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Birthdate = dto.Birthdate
        };

        _dbContext.Users.Add(user);
        await _dbContext.SaveChangesAsync();

        var verificationToken = await _emailVerificationService.GenerateTokenAsync(user.Id);

        await _emailVerificationService.SendVerificationEmailAsync(user.Id, user.Email, verificationToken);

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogInformation(
            "User {UserId} registered successfully.", 
            user.Id);

        return Ok(new AuthResponseDto
        {
            Token = token,
            ExpiresAt = expiresAt,
            IsEmailVerified = user.IsEmailVerified
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

        if (!user.IsEmailVerified)
        {
            _logger.LogWarning(
                "Failed to verify email {Email}",
                dto.Email);
            
            return Unauthorized("Please verify your email.");
        }

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogInformation(
            "User {UserId} logged in successfully.",
            user.Id);

        return Ok(new 
        {
            Token = token,
            ExpiresAt = expiresAt,
            user = new UserResponseDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                IsEmailVerified = user.IsEmailVerified,
                Birthdate = user.Birthdate,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        });
    }

    [HttpPost("verify")]
    public async Task<ActionResult> VerifyEmail([FromQuery] string token)
    {
        var userId = await _emailVerificationService.VerifyTokenAsync(token);

        if (userId == null)
        {
            return BadRequest("Invalid or expired verification token.");
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return BadRequest("User not found.");
        }

        var (Token, ExpiresAt) = _jwtService.GenerateToken(user);

        return Ok(new
        {
            token = Token,
            expiresAt = ExpiresAt,
            user = new UserResponseDto
            {
                Id = user.Id,
                Name = user.Name,
                Username = user.Username,
                Email = user.Email,
                IsEmailVerified = user.IsEmailVerified,
                CreatedAt = DateTime.UtcNow,
                UpdatedAt = DateTime.UtcNow
            }
        });
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult> ResendVerificationEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest("Email is required.");
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return NotFound("User not found.");
        }

        var result = await _emailVerificationService.ResendVerificationEmailAsync(user.Id, email);

        if (!result.Success)
        {
            return BadRequest(new { Message = result.Message });
        }

        return Ok(new
        {
            Message = result.Message
        });
    }
}
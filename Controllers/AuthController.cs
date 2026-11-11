using MarbookApi.Data;
using MarbookApi.DTOs;
using MarbookApi.Extensions;
using MarbookApi.Models;
using MarbookApi.Services;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using System.Security.Cryptography;

namespace MarbookApi.Controllers;

[ApiController]
[Route("api/[controller]")]
[EnableRateLimiting("auth-policy")]
public class AuthController(
    AppDbContext dbContext,
    IJwtService jwtService,
    ILogger<AuthController> logger,
    IEmailVerificationService emailVerificationService,
    IBrevoEmailSender brevoEmailSender) : ControllerBase
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IJwtService _jwtService = jwtService;
    private readonly ILogger _logger = logger;
    private readonly IEmailVerificationService _emailVerificationService = emailVerificationService;
    private readonly IBrevoEmailSender _brevoEmailSender = brevoEmailSender;

    [HttpPost("register")]
    public async Task<ActionResult<AuthResponseDto>> Register(RegisterDto dto)
    {
        var trimmedEmail = dto.Email.Trim();
        var emailExist = await _dbContext.Users.AnyAsync(u => u.Email == trimmedEmail);

        if (emailExist)
        {
            return Conflict(new { message = "Email is already registered. Please check your inbox to verify or log in." });
        }

        var trimmedUsername = dto.Username.Trim().ToLowerInvariant();
        var reservedWords = new HashSet<string>(StringComparer.OrdinalIgnoreCase)
        {
            "login", "register", "verify", "feed", "settings", "messages",
            "notifications", "search", "api", "admin", "profile", "me", "auth",
            "about", "help", "support", "explore"
        };

        if (reservedWords.Contains(trimmedUsername))
        {
            return BadRequest(new { message = "This username is reserved. Please choose another." });
        }

        var usernameExist = await _dbContext.Users.AnyAsync(u => u.Username != null && u.Username.ToLower() == trimmedUsername);
        if (usernameExist)
        {
            return Conflict(new { message = "Username is already taken. Please choose another." });
        }

        var user = new User
        {
            Name = dto.Name.Trim(),
            Email = trimmedEmail,
            IsEmailVerified = false,
            Username = trimmedUsername,
            PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.Password),
            Birthdate = dto.Birthdate,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };

        _dbContext.Users.Add(user);
        try
        {
            await _dbContext.SaveChangesAsync();
        }
        catch (DbUpdateException ex)
        {
            _logger.LogWarning(
                ex,
                "Concurrent duplicate registration for email {Email} or username {Username}",
                trimmedEmail, trimmedUsername);
            return Conflict(new { message = "An account with this email or username already exists. Please check your inbox or log in." });
        }

        var verificationToken = await _emailVerificationService.GenerateTokenAsync(user.Id);

        await _emailVerificationService.SendVerificationEmailAsync(user.Id, user.Email, verificationToken);

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogUserAction("UserRegistered", user.Id, user.Username, "User", user.Id, $"Email={user.Email}");

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
            _logger.LogSecurityWarning("LoginFailed", user?.Id, user?.Username, "Auth", dto.Email, "Invalid credentials");

            return Unauthorized("Invalid email or password.");
        }

        if (!user.IsEmailVerified)
        {
            _logger.LogSecurityWarning("LoginBlockedUnverified", user.Id, user.Username, "Auth", dto.Email, "Email not verified");
            
            return Unauthorized("Please verify your email.");
        }

        var (token, expiresAt) = _jwtService.GenerateToken(user);

        _logger.LogUserAction("UserLoggedIn", user.Id, user.Username, "Auth", user.Id);

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
                IsBirthdatePrivate = user.IsBirthdatePrivate,
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
            _logger.LogSecurityWarning("EmailVerificationFailed", null, null, "Auth", null, "Invalid or expired verification token");
            return BadRequest(new { message = "Invalid or expired verification token." });
        }

        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null)
        {
            return BadRequest(new { message = "User not found." });
        }

        var (Token, ExpiresAt) = _jwtService.GenerateToken(user);

        _logger.LogUserAction("EmailVerified", user.Id, user.Username, "User", user.Id);

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
                Bio = user.Bio,
                AvatarUrl = user.AvatarUrl,
                CoverUrl = user.CoverUrl,
                Birthdate = user.Birthdate,
                IsBirthdatePrivate = user.IsBirthdatePrivate,
                IsEmailVerified = user.IsEmailVerified,
                CreatedAt = user.CreatedAt,
                UpdatedAt = user.UpdatedAt
            }
        });
    }

    [HttpPost("resend-verification")]
    public async Task<ActionResult> ResendVerificationEmail([FromQuery] string email)
    {
        if (string.IsNullOrWhiteSpace(email))
        {
            return BadRequest(new { message = "Email is required." });
        }

        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email == email);
        if (user == null)
        {
            return NotFound(new { message = "User not found." });
        }

        var result = await _emailVerificationService.ResendVerificationEmailAsync(user.Id, email);

        if (!result.Success)
        {
            return BadRequest(new { Message = result.Message });
        }

        _logger.LogUserAction("VerificationEmailResent", user.Id, user.Username, "User", user.Id);

        return Ok(new
        {
            Message = result.Message
        });
    }

    [HttpPost("forgot-password")]
    [EnableRateLimiting("password-reset-policy")]
    public async Task<ActionResult> ForgotPassword([FromBody] ForgotPasswordDto dto)
    {
        var genericMessage = "If an account with that email exists, password reset instructions have been sent.";

        if (string.IsNullOrWhiteSpace(dto.Email))
        {
            return Ok(new { message = genericMessage });
        }

        var trimmedEmail = dto.Email.Trim().ToLowerInvariant();
        var user = await _dbContext.Users.FirstOrDefaultAsync(u => u.Email.ToLower() == trimmedEmail);

        if (user == null)
        {
            // Do not reveal whether user exists (prevents user enumeration attacks)
            return Ok(new { message = genericMessage });
        }

        // Invalidate prior unused password resets for this user
        var oldResets = await _dbContext.PasswordResets
            .Where(pr => pr.UserId == user.Id && !pr.IsUsed)
            .ToListAsync();

        foreach (var oldReset in oldResets)
        {
            oldReset.IsUsed = true;
        }

        // Generate cryptographically secure token
        var tokenBytes = RandomNumberGenerator.GetBytes(32);
        var token = Convert.ToBase64String(tokenBytes);

        var passwordReset = new PasswordReset
        {
            UserId = user.Id,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(1),
            IsUsed = false,
            CreatedAt = DateTime.UtcNow
        };

        _dbContext.PasswordResets.Add(passwordReset);
        await _dbContext.SaveChangesAsync();

        try
        {
            await _brevoEmailSender.SendPasswordResetEmailAsync(user.Id, user.Email, token);
            _logger.LogUserAction("PasswordResetRequested", user.Id, user.Username, "User", user.Id);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to send password reset email to {Email}", user.Email);
        }

        return Ok(new { message = genericMessage });
    }

    [HttpPost("reset-password")]
    public async Task<ActionResult> ResetPassword([FromBody] ResetPasswordDto dto)
    {
        if (string.IsNullOrWhiteSpace(dto.Token) || string.IsNullOrWhiteSpace(dto.NewPassword))
        {
            return BadRequest(new { message = "Token and new password are required." });
        }

        // Handle space vs '+' decoding quirks in URL-encoded tokens
        var cleanToken = dto.Token.Trim();
        var tokenVariant = cleanToken.Replace(" ", "+");

        var resetEntry = await _dbContext.PasswordResets
            .Include(pr => pr.User)
            .FirstOrDefaultAsync(pr =>
                (pr.Token == cleanToken || pr.Token == tokenVariant) &&
                !pr.IsUsed &&
                pr.ExpiresAt > DateTime.UtcNow);

        if (resetEntry == null)
        {
            return BadRequest(new { message = "Invalid or expired password reset link. Please request a new one." });
        }

        var user = resetEntry.User ?? await _dbContext.Users.FindAsync(resetEntry.UserId);
        if (user == null)
        {
            return BadRequest(new { message = "User not found." });
        }

        // Update password hash
        user.PasswordHash = BCrypt.Net.BCrypt.HashPassword(dto.NewPassword);
        user.UpdatedAt = DateTime.UtcNow;

        // Mark reset token as used
        resetEntry.IsUsed = true;

        await _dbContext.SaveChangesAsync();

        _logger.LogUserAction("PasswordResetCompleted", user.Id, user.Username, "User", user.Id);

        return Ok(new { message = "Password has been reset successfully. You can now log in with your new password." });
    }
}
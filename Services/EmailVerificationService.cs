using MarbookApi.Data;
using Microsoft.EntityFrameworkCore;
using MarbookApi.Models;

public class EmailVerificationService(AppDbContext dbContext, IBrevoEmailSender brevoEmailSender, ILogger<EmailVerificationService> logger) : IEmailVerificationService
{
    private readonly AppDbContext _dbContext = dbContext;
    private readonly IBrevoEmailSender _brevoEmailSender = brevoEmailSender;
    private readonly ILogger _logger = logger;

    public async Task<string> GenerateTokenAsync(int userId)
    {
        // Clean up any expired/unused tokens first
        await _dbContext.EmailVerifications
            .Where(v => v.UserId == userId && v.ExpiresAt <= DateTime.UtcNow)
            .ExecuteDeleteAsync();

        var token = Convert.ToBase64String(Guid.NewGuid().ToByteArray());
        var verification = new EmailVerification
        {
            UserId = userId,
            Token = token,
            ExpiresAt = DateTime.UtcNow.AddHours(24),
            IsUsed = false
        };
        _dbContext.EmailVerifications.Add(verification);
        await _dbContext.SaveChangesAsync();
        return token;
    }

    public async Task SendVerificationEmailAsync(int userId, string email, string token)
    {
        try
        {
            await _brevoEmailSender.SendVerificationEmailAsync(userId, email, token);
            _logger.LogInformation(
                "Verification email sent to {Email} for user {UserId}",
                email, userId);
        }
        catch (Exception ex)
        {
            _logger.LogError(
                ex,
                "Failed to send verification email to {Email} for user {UserId}",
                email, userId);
        }
    }

    public async Task<int?> VerifyTokenAsync(string token)
    {
        var verification = await _dbContext.EmailVerifications
            .FirstOrDefaultAsync(v => v.Token == token && !v.IsUsed && v.ExpiresAt > DateTime.UtcNow);

        if (verification == null) return null;

        var user = await _dbContext.Users.FindAsync(verification.UserId);
        if (user == null) return null;

        user.IsEmailVerified = true;
        verification.IsUsed = true;
        user.VerifiedAt = DateTime.UtcNow;
        user.VerificationToken = null;

        await _dbContext.SaveChangesAsync();
        return user.Id;
    }

    public async Task<(bool Success, string? Token, string? Message)> ResendVerificationEmailAsync(int userId, string email)
    {
        // Check if user is already verified
        var user = await _dbContext.Users.FindAsync(userId);
        if (user == null) return (false, null, "User not found.");

        if (user.IsEmailVerified)
        {
            return (false, null, "Email is already verified.");
        }

        // Clean up old tokens and generate new one
        var tokenResult = await GenerateTokenAsync(userId);

        await SendVerificationEmailAsync(userId, email, tokenResult);

        return (true, tokenResult, "Verification email resent successfully.");
    }
}
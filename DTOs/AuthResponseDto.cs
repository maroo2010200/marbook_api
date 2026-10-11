namespace MarbookApi.DTOs;

public class AuthResponseDto
{
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsEmailVerified { get; set; }
    public string? VerificationToken { get; set; }
}
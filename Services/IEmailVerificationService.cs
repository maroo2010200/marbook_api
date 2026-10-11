public interface IEmailVerificationService
{
    Task<string> GenerateTokenAsync(int userId);
    Task SendVerificationEmailAsync(int userId, string email, string token);
    Task<int?> VerifyTokenAsync(string token);
}
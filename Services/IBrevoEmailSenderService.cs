public interface IBrevoEmailSender
{
    Task SendVerificationEmailAsync(int userId, string email, string verificationToken);
}
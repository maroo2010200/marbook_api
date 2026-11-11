public interface IBrevoEmailSender
{
    Task SendVerificationEmailAsync(int userId, string email, string verificationToken);
    Task SendPasswordResetEmailAsync(int userId, string email, string resetToken);
}
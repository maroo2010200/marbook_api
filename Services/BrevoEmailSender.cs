using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class BrevoEmailSender : IBrevoEmailSender
{
    private const string BrevoEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public BrevoEmailSender(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task SendVerificationEmailAsync(int userId, string email, string verificationToken)
    {
        var encodedToken = Uri.EscapeDataString(verificationToken);
        var frontendVerificationUrl = $"{GetFrontendBaseUrl()}/verify?token={encodedToken}";

        var htmlContent = BuildBrandedEmailHtml(
            heading: "Welcome to Marshmallow! 🔥",
            subtitle: "Thanks for pulling up a chair by the campfire.",
            bodyHtml: "Click the button below to verify your email and activate your account. This link will expire in <strong>24 hours</strong>.",
            ctaLabel: "Verify Email →",
            ctaUrl: frontendVerificationUrl,
            footerText: "If you didn't create a Marshmallow account, you can safely ignore this email.");

        await SendEmailAsync(email, "Verify your email on Marshmallow", htmlContent);
    }

    public async Task SendPasswordResetEmailAsync(int userId, string email, string resetToken)
    {
        var encodedToken = Uri.EscapeDataString(resetToken);
        var frontendResetUrl = $"{GetFrontendBaseUrl()}/reset-password?token={encodedToken}";

        var htmlContent = BuildBrandedEmailHtml(
            heading: "Reset Your Password 🔑",
            subtitle: "Someone requested a password reset for your Marshmallow account.",
            bodyHtml: "If this was you, click the button below to set a new password. This link will expire in <strong>1 hour</strong>.",
            ctaLabel: "Reset Password →",
            ctaUrl: frontendResetUrl,
            footerText: "If you didn't request this reset, you can safely ignore this email. Your campfire password will remain unchanged.");

        await SendEmailAsync(email, "Reset your Marshmallow password", htmlContent);
    }

    private string GetFrontendBaseUrl() =>
        _configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5173";

    private static string BuildBrandedEmailHtml(
        string heading,
        string subtitle,
        string bodyHtml,
        string ctaLabel,
        string ctaUrl,
        string footerText)
    {
        return $@"<div style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; max-width: 520px; margin: 0 auto; padding: 24px; background-color: #FAF4EB; border-radius: 16px; border: 1px solid #E9B97C;'>
                                <div style='text-align: center; margin-bottom: 20px;'>
                                    <h1 style='color: #451A03; font-size: 24px; margin: 0 0 8px 0;'>{heading}</h1>
                                    <p style='color: #78350F; font-size: 14px; margin: 0;'>{subtitle}</p>
                                </div>
                                <div style='background-color: #FFFFFF; padding: 24px; border-radius: 12px; border: 1px solid #E9B97C; text-align: center;'>
                                    <p style='color: #451A03; font-size: 14px; line-height: 1.6; margin: 0 0 20px 0;'>
                                        {bodyHtml}
                                    </p>
                                    <a href='{ctaUrl}' style='display: inline-block; padding: 12px 28px; background-color: #C96B32; color: #FFFFFF; text-decoration: none; font-weight: bold; font-size: 15px; border-radius: 9999px; box-shadow: 0 4px 12px rgba(201, 107, 50, 0.35);'>
                                        {ctaLabel}
                                    </a>
                                </div>
                                <p style='color: #92400E; font-size: 12px; text-align: center; margin: 20px 0 0 0;'>
                                    {footerText}
                                </p>
                             </div>";
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
    {
        var apiKey = _configuration["Brevo:ApiKey"]
                       ?? throw new InvalidOperationException("Brevo API key is not configured.");

        var emailData = new
        {
            sender = new
            {
                name = "Marshmallow",
                email = "maroo30303030@gmail.com"
            },
            to = new[]
            {
                new { email = toEmail, name = "Marshmallow User" }
            },
            subject,
            htmlContent
        };

        var json = JsonSerializer.Serialize(emailData);
        using var content = new StringContent(json, Encoding.UTF8, "application/json");
        using var request = new HttpRequestMessage(HttpMethod.Post, BrevoEndpoint);
        request.Headers.Add("api-key", apiKey);
        request.Content = content;

        using var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Brevo API error: {error}");
        }
    }
}
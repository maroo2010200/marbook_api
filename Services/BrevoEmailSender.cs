using System.Net.Http;
using System.Net;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class BrevoEmailSender : IBrevoEmailSender
{
    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;

    public BrevoEmailSender(HttpClient httpClient, IConfiguration configuration)
    {
        _httpClient = httpClient;
        _configuration = configuration;
    }

    public async Task SendVerificationEmailAsync(int userId, string email, string verificationToken)
    {
        var apiKey = _configuration["Brevo:ApiKey"]
                       ?? throw new InvalidOperationException("Brevo API key is not configured.");

        string encodedToken = WebUtility.UrlEncode(verificationToken);

        var frontendBaseUrl = _configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5173";
        string frontendVerificationUrl = $"{frontendBaseUrl}/verify?token={encodedToken}";

        var emailData = new
        {
            sender = new
            {
                name = "Marshmallow",
                email = "maroo30303030@gmail.com"
            },
            to = new[]
            {
                new { email = email, name = "Marwan" }
            },
            subject = "Verify your email on Marshmallow",
            htmlContent = $@"<h1>Welcome to Marshmallow!</h1>
                             <p>Thanks for joining. Please verify your email to activate your account.</p>
                             <a href='{frontendVerificationUrl}'>
                               Click here to verify
                             </a>"
        };

        var json = JsonSerializer.Serialize(emailData);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Brevo API error: {error}");
        }
    }

    public async Task SendPasswordResetEmailAsync(int userId, string email, string resetToken)
    {
        var apiKey = _configuration["Brevo:ApiKey"]
                       ?? throw new InvalidOperationException("Brevo API key is not configured.");

        string encodedToken = Uri.EscapeDataString(resetToken);
        var frontendBaseUrl = _configuration["Frontend:BaseUrl"]?.TrimEnd('/') ?? "http://localhost:5173";
        string frontendResetUrl = $"{frontendBaseUrl}/reset-password?token={encodedToken}";

        var emailData = new
        {
            sender = new
            {
                name = "Marshmallow",
                email = "maroo30303030@gmail.com"
            },
            to = new[]
            {
                new { email = email, name = "Marshmallow User" }
            },
            subject = "Reset your Marshmallow password",
            htmlContent = $@"<div style='font-family: -apple-system, BlinkMacSystemFont, ""Segoe UI"", Roboto, Helvetica, Arial, sans-serif; max-width: 520px; margin: 0 auto; padding: 24px; background-color: #FAF4EB; border-radius: 16px; border: 1px solid #E9B97C;'>
                                <div style='text-align: center; margin-bottom: 20px;'>
                                    <h1 style='color: #451A03; font-size: 24px; margin: 0 0 8px 0;'>Reset Your Password 🔑</h1>
                                    <p style='color: #78350F; font-size: 14px; margin: 0;'>Someone requested a password reset for your Marshmallow account.</p>
                                </div>
                                <div style='background-color: #FFFFFF; padding: 24px; border-radius: 12px; border: 1px solid #E9B97C; text-align: center;'>
                                    <p style='color: #451A03; font-size: 14px; line-height: 1.6; margin: 0 0 20px 0;'>
                                        If this was you, click the button below to set a new password. This link will expire in <strong>1 hour</strong>.
                                    </p>
                                    <a href='{frontendResetUrl}' style='display: inline-block; padding: 12px 28px; background-color: #C96B32; color: #FFFFFF; text-decoration: none; font-weight: bold; font-size: 15px; border-radius: 9999px; box-shadow: 0 4px 12px rgba(201, 107, 50, 0.35);'>
                                        Reset Password →
                                    </a>
                                </div>
                                <p style='color: #92400E; font-size: 12px; text-align: center; margin: 20px 0 0 0;'>
                                    If you didn't request this reset, you can safely ignore this email. Your campfire password will remain unchanged.
                                </p>
                             </div>"
        };

        var json = JsonSerializer.Serialize(emailData);
        var content = new StringContent(json, Encoding.UTF8, "application/json");

        var request = new HttpRequestMessage(HttpMethod.Post, "https://api.brevo.com/v3/smtp/email");
        request.Headers.Add("api-key", apiKey);
        request.Content = content;

        var response = await _httpClient.SendAsync(request);

        if (!response.IsSuccessStatusCode)
        {
            var error = await response.Content.ReadAsStringAsync();
            throw new Exception($"Brevo API error: {error}");
        }
    }
}
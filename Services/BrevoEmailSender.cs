using System.Net;
using System.Net.Mail;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class BrevoEmailSender : IBrevoEmailSender
{
    private const string BrevoEndpoint = "https://api.brevo.com/v3/smtp/email";

    private readonly HttpClient _httpClient;
    private readonly IConfiguration _configuration;
    private readonly ILogger<BrevoEmailSender> _logger;

    public BrevoEmailSender(
        HttpClient httpClient,
        IConfiguration configuration,
        ILogger<BrevoEmailSender> logger)
    {
        _httpClient = httpClient;
        _configuration = configuration;
        _logger = logger;
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
        return $@"<!DOCTYPE html>
<html lang=""en"">
<head>
    <meta charset=""UTF-8"">
    <meta name=""viewport"" content=""width=device-width, initial-scale=1.0"">
    <meta http-equiv=""X-UA-Compatible"" content=""IE=edge"">
    <title>{heading}</title>
</head>
<body style=""margin: 0; padding: 0; background-color: #FAF4EB; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, Helvetica, Arial, sans-serif;"">
    <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""background-color: #FAF4EB; padding: 24px 12px;"">
        <tr>
            <td align=""center"">
                <table role=""presentation"" width=""100%"" cellspacing=""0"" cellpadding=""0"" border=""0"" style=""max-width: 520px; background-color: #FAF4EB; border-radius: 16px; border: 1px solid #E9B97C; padding: 24px;"">
                    <!-- Header -->
                    <tr>
                        <td align=""center"" style=""padding-bottom: 20px;"">
                            <h1 style=""color: #451A03; font-size: 24px; margin: 0 0 8px 0; font-weight: 700;"">{heading}</h1>
                            <p style=""color: #78350F; font-size: 14px; margin: 0;"">{subtitle}</p>
                        </td>
                    </tr>
                    <!-- Card Body -->
                    <tr>
                        <td style=""background-color: #FFFFFF; padding: 28px 24px; border-radius: 12px; border: 1px solid #E9B97C; text-align: center;"">
                            <p style=""color: #451A03; font-size: 15px; line-height: 1.6; margin: 0 0 24px 0;"">
                                {bodyHtml}
                            </p>
                            <!-- Bulletproof Button -->
                            <table role=""presentation"" cellspacing=""0"" cellpadding=""0"" border=""0"" align=""center"">
                                <tr>
                                    <td align=""center"" style=""border-radius: 9999px; background-color: #C96B32;"">
                                        <a href=""{ctaUrl}"" target=""_blank"" style=""display: inline-block; padding: 14px 32px; font-family: -apple-system, BlinkMacSystemFont, 'Segoe UI', Roboto, sans-serif; font-size: 15px; font-weight: bold; color: #FFFFFF; text-decoration: none; border-radius: 9999px; background-color: #C96B32; border: 1px solid #C96B32;"">
                                            {ctaLabel}
                                        </a>
                                    </td>
                                </tr>
                            </table>
                            <!-- Fallback Link -->
                            <div style=""margin-top: 24px; padding-top: 18px; border-top: 1px dashed #E9B97C; text-align: left;"">
                                <p style=""color: #78350F; font-size: 12px; line-height: 1.5; margin: 0 0 6px 0;"">
                                    If the button doesn't work, copy and paste this link into your browser:
                                </p>
                                <p style=""margin: 0; word-break: break-all;"">
                                    <a href=""{ctaUrl}"" target=""_blank"" style=""color: #C96B32; font-size: 12px; text-decoration: underline;"">
                                        {ctaUrl}
                                    </a>
                                </p>
                            </div>
                        </td>
                    </tr>
                    <!-- Footer -->
                    <tr>
                        <td align=""center"" style=""padding-top: 20px;"">
                            <p style=""color: #92400E; font-size: 12px; margin: 0; line-height: 1.4;"">
                                {footerText}
                            </p>
                        </td>
                    </tr>
                </table>
            </td>
        </tr>
    </table>
</body>
</html>";
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
    {
        var smtpPassword = _configuration["Smtp:AppPassword"];
        var smtpEmail = _configuration["Smtp:SenderEmail"] ?? "maroo30303030@gmail.com";
        var smtpHost = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
        var smtpPort = int.TryParse(_configuration["Smtp:Port"], out var port) ? port : 587;

        if (!string.IsNullOrWhiteSpace(smtpPassword))
        {
            var cleanPassword = smtpPassword.Replace(" ", "").Trim();
            _logger.LogInformation(
                "Dispatching email to {Email} via Google Direct SMTP ({Host}:{Port})...",
                toEmail, smtpHost, smtpPort);

            // Send via Google SMTP (Direct Google DKIM signature -> Lands in Primary Inbox)
            using var client = new SmtpClient(smtpHost, smtpPort)
            {
                EnableSsl = true,
                Credentials = new NetworkCredential(smtpEmail, cleanPassword)
            };

            using var mail = new MailMessage
            {
                From = new MailAddress(smtpEmail, "Marshmallow"),
                Subject = subject,
                Body = htmlContent,
                IsBodyHtml = true
            };
            mail.To.Add(toEmail);

            await client.SendMailAsync(mail);
            _logger.LogInformation("Email successfully delivered to {Email} via Google SMTP.", toEmail);
            return;
        }

        _logger.LogWarning(
            "Smtp:AppPassword is not configured. Falling back to Brevo API for {Email} (may land in spam without custom domain DKIM)...",
            toEmail);

        // Fallback to Brevo API if Smtp:AppPassword is not set
        var apiKey = _configuration["Brevo:ApiKey"]
                       ?? throw new InvalidOperationException("Neither SMTP nor Brevo API is configured.");

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
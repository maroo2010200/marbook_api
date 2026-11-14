using System.Net;
using System.Net.Mail;
using System.Net.Http;
using System.Text;
using System.Text.Json;
using System.Threading.Tasks;

public class BrevoEmailSender : IBrevoEmailSender
{
    private const string BrevoEndpoint = "https://api.brevo.com/v3/smtp/email";

    private static string? _cachedAccessToken;
    private static DateTime _tokenExpiry = DateTime.MinValue;
    private static readonly SemaphoreSlim _tokenLock = new(1, 1);

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

    private async Task<string> GetGmailAccessTokenAsync(string clientId, string clientSecret, string refreshToken)
    {
        if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
        {
            return _cachedAccessToken;
        }

        await _tokenLock.WaitAsync();
        try
        {
            if (!string.IsNullOrWhiteSpace(_cachedAccessToken) && DateTime.UtcNow < _tokenExpiry)
            {
                return _cachedAccessToken;
            }

            using var tokenRequest = new HttpRequestMessage(HttpMethod.Post, "https://oauth2.googleapis.com/token")
            {
                Content = new FormUrlEncodedContent(new Dictionary<string, string>
                {
                    ["client_id"] = clientId,
                    ["client_secret"] = clientSecret,
                    ["refresh_token"] = refreshToken,
                    ["grant_type"] = "refresh_token"
                })
            };

            using var response = await _httpClient.SendAsync(tokenRequest);
            var responseJson = await response.Content.ReadAsStringAsync();

            if (!response.IsSuccessStatusCode)
            {
                throw new InvalidOperationException($"Failed to refresh Gmail access token: {responseJson}");
            }

            using var doc = JsonDocument.Parse(responseJson);
            var accessToken = doc.RootElement.GetProperty("access_token").GetString()
                ?? throw new InvalidOperationException("access_token was null in Google OAuth response.");

            var expiresIn = doc.RootElement.TryGetProperty("expires_in", out var expProp) ? expProp.GetInt32() : 3600;

            _cachedAccessToken = accessToken;
            _tokenExpiry = DateTime.UtcNow.AddSeconds(Math.Max(60, expiresIn - 120));

            return accessToken;
        }
        finally
        {
            _tokenLock.Release();
        }
    }

    private async Task SendViaGmailApiAsync(
        string toEmail,
        string subject,
        string htmlContent,
        string clientId,
        string clientSecret,
        string refreshToken,
        string senderEmail)
    {
        var accessToken = await GetGmailAccessTokenAsync(clientId, clientSecret, refreshToken);

        var rfc822 = $"From: Marshmallow <{senderEmail}>\r\n" +
                     $"To: {toEmail}\r\n" +
                     $"Subject: {subject}\r\n" +
                     "MIME-Version: 1.0\r\n" +
                     "Content-Type: text/html; charset=utf-8\r\n\r\n" +
                     htmlContent;

        var rawBytes = Encoding.UTF8.GetBytes(rfc822);
        var base64Url = Convert.ToBase64String(rawBytes)
            .Replace('+', '-')
            .Replace('/', '_')
            .TrimEnd('=');

        var payload = JsonSerializer.Serialize(new { raw = base64Url });
        using var request = new HttpRequestMessage(HttpMethod.Post, "https://gmail.googleapis.com/gmail/v1/users/me/messages/send")
        {
            Content = new StringContent(payload, Encoding.UTF8, "application/json")
        };
        request.Headers.Authorization = new System.Net.Http.Headers.AuthenticationHeaderValue("Bearer", accessToken);

        using var response = await _httpClient.SendAsync(request);
        if (!response.IsSuccessStatusCode)
        {
            var err = await response.Content.ReadAsStringAsync();
            throw new InvalidOperationException($"Gmail API error ({response.StatusCode}): {err}");
        }

        _logger.LogInformation("Email successfully delivered to {Email} via Google Gmail REST API (Primary Inbox guaranteed).", toEmail);
    }

    private async Task SendEmailAsync(string toEmail, string subject, string htmlContent)
    {
        var gmailClientId = _configuration["Gmail:ClientId"];
        var gmailClientSecret = _configuration["Gmail:ClientSecret"];
        var gmailRefreshToken = _configuration["Gmail:RefreshToken"];
        var gmailSenderEmail = _configuration["Gmail:SenderEmail"] ?? "maroo30303030@gmail.com";

        // Priority 1: Google Gmail REST API (HTTPS port 443 -> Works on Render Free Tier + Lands 100% in Primary Inbox)
        if (!string.IsNullOrWhiteSpace(gmailClientId) &&
            !string.IsNullOrWhiteSpace(gmailClientSecret) &&
            !string.IsNullOrWhiteSpace(gmailRefreshToken))
        {
            try
            {
                _logger.LogInformation("Dispatching email to {Email} via Google Gmail REST API (Port 443)...", toEmail);
                await SendViaGmailApiAsync(toEmail, subject, htmlContent, gmailClientId, gmailClientSecret, gmailRefreshToken, gmailSenderEmail);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(ex, "Gmail REST API dispatch failed for {Email}. Falling back to next email channel...", toEmail);
            }
        }

        var smtpPassword = _configuration["Smtp:AppPassword"];
        var smtpEmail = _configuration["Smtp:SenderEmail"] ?? "maroo30303030@gmail.com";
        var smtpHost = _configuration["Smtp:Host"] ?? "smtp.gmail.com";
        var smtpPort = int.TryParse(_configuration["Smtp:Port"], out var port) ? port : 587;

        var isRender = !string.IsNullOrWhiteSpace(Environment.GetEnvironmentVariable("RENDER"));

        // Priority 2: Google Direct SMTP (Port 587) - Skip on Render Free Tier where SMTP is blocked
        if (!isRender && !string.IsNullOrWhiteSpace(smtpPassword))
        {
            try
            {
                var cleanPassword = smtpPassword.Replace(" ", "").Trim();
                _logger.LogInformation(
                    "Dispatching email to {Email} via Google Direct SMTP ({Host}:{Port})...",
                    toEmail, smtpHost, smtpPort);

                using var client = new SmtpClient(smtpHost, smtpPort)
                {
                    EnableSsl = true,
                    Credentials = new NetworkCredential(smtpEmail, cleanPassword),
                    Timeout = 5000
                };

                using var mail = new MailMessage
                {
                    From = new MailAddress(smtpEmail, "Marshmallow"),
                    Subject = subject,
                    Body = htmlContent,
                    IsBodyHtml = true
                };
                mail.To.Add(toEmail);

                await client.SendMailAsync(mail).WaitAsync(TimeSpan.FromSeconds(5));
                _logger.LogInformation("Email successfully delivered to {Email} via Google SMTP.", toEmail);
                return;
            }
            catch (Exception ex)
            {
                _logger.LogWarning(
                    ex,
                    "Google SMTP failed or timed out for {Email}. Falling back to Brevo HTTP API...",
                    toEmail);
            }
        }

        _logger.LogInformation(
            isRender
                ? "Running on Render: SMTP port 587 blocked by cloud firewall. Using Brevo HTTP API (Port 443) for {Email}..."
                : "Dispatching email to {Email} via Brevo HTTP API (Port 443)...",
            toEmail);

        // Priority 3: Brevo REST API (HTTPS Port 443 fallback)
        var apiKey = _configuration["Brevo:ApiKey"]
                       ?? throw new InvalidOperationException("Neither Gmail API, SMTP, nor Brevo API is configured.");

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

        _logger.LogInformation("Email successfully delivered to {Email} via Brevo HTTP API.", toEmail);
    }
}
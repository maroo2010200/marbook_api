using System.Net.Http;
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

        var emailData = new
        {
            sender = new
            {
                name = "Marbook",
                email = "maroo30303030@gmail.com"
            },
            to = new[]
            {
                new { email = email, name = "Marwan" }
            },
            subject = "Verify your email on Marbook",
            htmlContent = $@"<h1>Welcome to Marbook!</h1>
                             <p>Thanks for joining. Please verify your email to activate your account.</p>
                             <a href='http://localhost:5244/api/auth/verify?token={verificationToken}'>
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
}
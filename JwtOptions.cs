namespace MarbookApi;

public class JwtOptions
{
    public string Issuer { get; set; } = string.Empty;
    public string Audience { get; set; } = string.Empty;
    public int LifetimeInMinutes { get; set; }
    public string SigningKey { get; set; } = string.Empty;
}
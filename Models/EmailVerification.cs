namespace MarbookApi.Models;
public class EmailVerification
{
    public int Id { get; set; }
    public int UserId { get; set; }
    public string Token { get; set; } = string.Empty;
    public DateTime ExpiresAt { get; set; }
    public bool IsUsed { get; set; }

    // Navigation property (optional, but useful)
    public User User { get; set; } = null!;
}
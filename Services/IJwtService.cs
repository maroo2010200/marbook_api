using MarbookApi.Models;

namespace MarbookApi.Services;

public interface IJwtService
{
    (string Token, DateTime ExpiresAt) GenerateToken(User user);
}
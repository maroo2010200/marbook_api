using MarbookApi.Models;

namespace MarbookApi.Services;

public interface IJwtService
{
    string GenerateToken(User user);
}
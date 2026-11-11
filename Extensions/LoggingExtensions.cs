using System.Security.Claims;

namespace MarbookApi.Extensions;

public static class LoggingExtensions
{
    public static int? GetUserIdOrNull(this ClaimsPrincipal? user)
    {
        var raw = user?.FindFirstValue(ClaimTypes.NameIdentifier);
        return int.TryParse(raw, out var id) ? id : null;
    }

    public static string GetUsernameOrDefault(this ClaimsPrincipal? user)
    {
        var name = user?.FindFirstValue(ClaimTypes.Name);
        return string.IsNullOrWhiteSpace(name) ? "unknown" : name;
    }

    public static string GetClientIp(this HttpContext context)
    {
        var forwardedFor = context.Request.Headers["X-Forwarded-For"].FirstOrDefault();
        if (!string.IsNullOrWhiteSpace(forwardedFor))
        {
            var firstIp = forwardedFor.Split(',')[0].Trim();
            if (!string.IsNullOrWhiteSpace(firstIp))
            {
                return firstIp;
            }
        }

        return context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
    }

    public static void LogUserAction(
        this ILogger logger,
        string action,
        int? userId,
        string? username,
        string targetType,
        object? targetId,
        string? details = null)
    {
        logger.LogInformation(
            "Action={Action} UserId={UserId} Username={Username} TargetType={TargetType} TargetId={TargetId} Details={Details}",
            action,
            userId?.ToString() ?? "anonymous",
            string.IsNullOrWhiteSpace(username) ? "unknown" : username,
            targetType,
            targetId?.ToString() ?? "-",
            string.IsNullOrWhiteSpace(details) ? "-" : details);
    }

    public static void LogSecurityWarning(
        this ILogger logger,
        string action,
        int? userId,
        string? username,
        string targetType,
        object? targetId,
        string? reason = null)
    {
        logger.LogWarning(
            "Action={Action} UserId={UserId} Username={Username} TargetType={TargetType} TargetId={TargetId} Reason={Reason}",
            action,
            userId?.ToString() ?? "anonymous",
            string.IsNullOrWhiteSpace(username) ? "unknown" : username,
            targetType,
            targetId?.ToString() ?? "-",
            string.IsNullOrWhiteSpace(reason) ? "-" : reason);
    }
}


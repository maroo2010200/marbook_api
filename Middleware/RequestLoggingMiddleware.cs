using System.Diagnostics;
using MarbookApi.Extensions;

namespace MarbookApi.Middleware;

public class RequestLoggingMiddleware(RequestDelegate next, ILogger<RequestLoggingMiddleware> logger)
{
    private static readonly HashSet<string> MutationMethods = new(StringComparer.OrdinalIgnoreCase)
    {
        HttpMethods.Post,
        HttpMethods.Put,
        HttpMethods.Patch,
        HttpMethods.Delete
    };

    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.GetUserIdOrNull();
        var username = userId.HasValue ? context.User.GetUsernameOrDefault() : "anonymous";
        var clientIp = context.GetClientIp();

        using var scope = logger.BeginScope(new Dictionary<string, object?>
        {
            ["RequestId"] = context.TraceIdentifier,
            ["UserId"] = userId?.ToString() ?? "anonymous",
            ["Username"] = username,
            ["ClientIp"] = clientIp
        });

        // SignalR WebSocket connections stay open continuously; ChatHub logs connect/disconnect events directly.
        if (context.WebSockets.IsWebSocketRequest)
        {
            await next(context);
            return;
        }

        var stopwatch = Stopwatch.StartNew();
        try
        {
            await next(context);
            stopwatch.Stop();

            LogCompletedRequest(
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                context.Response.StatusCode,
                stopwatch.ElapsedMilliseconds,
                userId,
                username,
                clientIp);
        }
        catch (Exception)
        {
            stopwatch.Stop();
            logger.LogError(
                "HTTP {Method} {Path} failed with unhandled exception after {ElapsedMs}ms | UserId={UserId} Username={Username} ClientIp={ClientIp}",
                context.Request.Method,
                context.Request.Path.Value ?? "/",
                stopwatch.ElapsedMilliseconds,
                userId?.ToString() ?? "anonymous",
                username,
                clientIp);
            throw;
        }
    }

    private void LogCompletedRequest(
        string method,
        string path,
        int statusCode,
        long elapsedMs,
        int? userId,
        string username,
        string clientIp)
    {
        var actorId = userId?.ToString() ?? "anonymous";

        if (statusCode >= StatusCodes.Status500InternalServerError)
        {
            logger.LogError(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms | UserId={UserId} Username={Username} ClientIp={ClientIp}",
                method, path, statusCode, elapsedMs, actorId, username, clientIp);
            return;
        }

        if (statusCode >= StatusCodes.Status400BadRequest)
        {
            logger.LogWarning(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms | UserId={UserId} Username={Username} ClientIp={ClientIp}",
                method, path, statusCode, elapsedMs, actorId, username, clientIp);
            return;
        }

        if (MutationMethods.Contains(method))
        {
            logger.LogInformation(
                "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms | UserId={UserId} Username={Username} ClientIp={ClientIp}",
                method, path, statusCode, elapsedMs, actorId, username, clientIp);
            return;
        }

        logger.LogDebug(
            "HTTP {Method} {Path} responded {StatusCode} in {ElapsedMs}ms | UserId={UserId} Username={Username} ClientIp={ClientIp}",
            method, path, statusCode, elapsedMs, actorId, username, clientIp);
    }
}


using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

namespace MarbookApi.Exceptions;

public class GlobalExceptionHandler : IExceptionHandler
{
    private readonly ILogger<GlobalExceptionHandler> _logger;

    public GlobalExceptionHandler(ILogger<GlobalExceptionHandler> logger)
    {
        _logger = logger;
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken
    )

    {
        _logger.LogError(
            exception,
            "An unhandled exception occurred: {Message}",
            exception.Message);

        httpContext.Response.StatusCode = StatusCodes.Status500InternalServerError;

        await httpContext.Response.WriteAsJsonAsync(
            new ProblemDetails
            {
                Status = StatusCodes.Status500InternalServerError,
                Title = "An unexpected error occured.",
            },
            cancellationToken);

        return true;
    }
}
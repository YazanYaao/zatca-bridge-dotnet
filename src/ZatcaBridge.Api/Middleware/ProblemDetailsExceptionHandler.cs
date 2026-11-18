using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace ZatcaBridge.Api.Middleware;

/// <summary>
/// Global exception handler generating RFC 7807 ProblemDetails for unhandled domain and application exceptions,
/// hardened to prevent sensitive stack trace and system details leaks in Production environments.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);
    private readonly IHostEnvironment _environment;
    private readonly ILogger<ProblemDetailsExceptionHandler> _logger;

    public ProblemDetailsExceptionHandler(
        IHostEnvironment environment,
        ILogger<ProblemDetailsExceptionHandler> logger)
    {
        _environment = environment ?? throw new ArgumentNullException(nameof(environment));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    public async ValueTask<bool> TryHandleAsync(
        HttpContext httpContext,
        Exception exception,
        CancellationToken cancellationToken)
    {
        var (statusCode, title) = exception switch
        {
            BadHttpRequestException => (StatusCodes.Status400BadRequest, "Malformed Request Payload"),
            JsonException => (StatusCodes.Status400BadRequest, "JSON Deserialization Error"),
            ArgumentNullException or ArgumentException => (StatusCodes.Status400BadRequest, "Invalid Argument"),
            InvalidOperationException => (StatusCodes.Status400BadRequest, "Domain Invariant Violation"),
            _ => (StatusCodes.Status500InternalServerError, "Internal Server Error")
        };

        string detail;
        if (statusCode >= 500)
        {
            _logger.LogError(exception, "Unhandled 500 exception occurred at {Path}. TraceIdentifier: {TraceId}",
                httpContext.Request.Path, httpContext.TraceIdentifier);

            detail = _environment.IsDevelopment()
                ? exception.Message
                : $"An unexpected server error occurred. Reference ID: {httpContext.TraceIdentifier}";
        }
        else
        {
            _logger.LogWarning("Handled {StatusCode} ({Title}) exception at {Path}: {Message}",
                statusCode, title, httpContext.Request.Path, exception.Message);

            detail = exception.Message;
        }

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = detail,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.io/{statusCode}"
        };

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problemDetails, JsonOptions, cancellationToken);

        return true;
    }
}

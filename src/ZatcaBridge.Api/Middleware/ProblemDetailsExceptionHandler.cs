using System;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;

namespace ZatcaBridge.Api.Middleware;

/// <summary>
/// Global exception handler generating RFC 7807 ProblemDetails for unhandled domain and application exceptions.
/// </summary>
public class ProblemDetailsExceptionHandler : IExceptionHandler
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

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

        var problemDetails = new ProblemDetails
        {
            Status = statusCode,
            Title = title,
            Detail = exception.Message,
            Instance = httpContext.Request.Path,
            Type = $"https://httpstatuses.io/{statusCode}"
        };

        httpContext.Response.StatusCode = statusCode;
        httpContext.Response.ContentType = "application/problem+json";
        await JsonSerializer.SerializeAsync(httpContext.Response.Body, problemDetails, JsonOptions, cancellationToken);

        return true;
    }
}

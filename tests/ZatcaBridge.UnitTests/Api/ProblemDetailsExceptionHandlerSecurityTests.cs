using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.FileProviders;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using ZatcaBridge.Api.Middleware;

namespace ZatcaBridge.UnitTests.Api;

public class ProblemDetailsExceptionHandlerSecurityTests
{
    private class TestHostEnvironment(string environmentName) : IHostEnvironment
    {
        public string EnvironmentName { get; set; } = environmentName;
        public string ApplicationName { get; set; } = "ZatcaBridge.Api";
        public string ContentRootPath { get; set; } = AppContext.BaseDirectory;
        public IFileProvider ContentRootFileProvider { get; set; } = new NullFileProvider();
    }

    [Fact]
    public async Task TryHandleAsync_InProduction_Redacts500InternalExceptionDetails()
    {
        // Arrange
        var prodEnv = new TestHostEnvironment(Environments.Production);
        var handler = new ProblemDetailsExceptionHandler(prodEnv, NullLogger<ProblemDetailsExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-test-12345";
        context.Response.Body = new MemoryStream();

        var sensitiveException = new Exception("DATABASE PASSWORD FAILED: postgres://user:secretpass@10.0.0.1/db");

        // Act
        var handled = await handler.TryHandleAsync(context, sensitiveException, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status500InternalServerError, context.Response.StatusCode);
        Assert.Equal("application/problem+json", context.Response.ContentType);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problemDetails);
        Assert.Equal(500, problemDetails.Status);
        Assert.Equal("Internal Server Error", problemDetails.Title);
        // Sensitive error message MUST be redacted in Production!
        Assert.DoesNotContain("secretpass", problemDetails.Detail);
        Assert.Contains("trace-test-12345", problemDetails.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_InDevelopment_ExposesExceptionMessageForDebugging()
    {
        // Arrange
        var devEnv = new TestHostEnvironment(Environments.Development);
        var handler = new ProblemDetailsExceptionHandler(devEnv, NullLogger<ProblemDetailsExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        const string devErrorMsg = "Detailed validation or syntax debug message";
        var devException = new Exception(devErrorMsg);

        // Act
        var handled = await handler.TryHandleAsync(context, devException, CancellationToken.None);

        // Assert
        Assert.True(handled);
        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problemDetails);
        Assert.Equal(devErrorMsg, problemDetails.Detail);
    }

    [Fact]
    public async Task TryHandleAsync_WithClientError_ReturnsStatus400()
    {
        // Arrange
        var prodEnv = new TestHostEnvironment(Environments.Production);
        var handler = new ProblemDetailsExceptionHandler(prodEnv, NullLogger<ProblemDetailsExceptionHandler>.Instance);

        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();

        var clientException = new ArgumentException("The 'SellerVatNumber' is required.");

        // Act
        var handled = await handler.TryHandleAsync(context, clientException, CancellationToken.None);

        // Assert
        Assert.True(handled);
        Assert.Equal(StatusCodes.Status400BadRequest, context.Response.StatusCode);

        context.Response.Body.Seek(0, SeekOrigin.Begin);
        var problemDetails = await JsonSerializer.DeserializeAsync<ProblemDetails>(context.Response.Body, new JsonSerializerOptions(JsonSerializerDefaults.Web));

        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.Equal("The 'SellerVatNumber' is required.", problemDetails.Detail);
    }
}

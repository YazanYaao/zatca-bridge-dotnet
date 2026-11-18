using System.Text.Json.Serialization;
using System.Threading.RateLimiting;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using ZatcaBridge.Api.Endpoints;
using ZatcaBridge.Api.Middleware;
using ZatcaBridge.Application.Extensions;
using ZatcaBridge.Infrastructure.Extensions;
using ZatcaBridge.Infrastructure.Gateway;
using ZatcaBridge.Infrastructure.Persistence;

var builder = WebApplication.CreateBuilder(args);

// Minimal API JSON serialization options
builder.Services.ConfigureHttpJsonOptions(options =>
{
    options.SerializerOptions.PropertyNameCaseInsensitive = true;
    options.SerializerOptions.Converters.Add(new JsonStringEnumConverter());
    options.SerializerOptions.DefaultIgnoreCondition = JsonIgnoreCondition.WhenWritingNull;
});

// RFC 7807 Problem Details & Exception Handler
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

// Rate Limiter for API endpoints protection
builder.Services.AddRateLimiter(options =>
{
    options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;
    options.AddFixedWindowLimiter("ApiFixedWindow", opt =>
    {
        opt.Window = TimeSpan.FromMinutes(1);
        opt.PermitLimit = 120;
        opt.QueueLimit = 0;
    });
});

// Production-safe configurable CORS
var allowedOrigins = builder.Configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()
    ?? ["http://localhost:5173", "http://127.0.0.1:5173"];

builder.Services.AddCors(options =>
{
    options.AddPolicy("PlaygroundDevCors", policy =>
    {
        policy.WithOrigins(allowedOrigins)
              .AllowAnyHeader()
              .AllowAnyMethod();
    });
});

// Clean Architecture Application & Infrastructure
builder.Services.AddZatcaApplication();
builder.Services.AddZatcaInfrastructure(
    connectionString: builder.Configuration.GetConnectionString("DefaultConnection") ?? "Data Source=zatca_bridge.db",
    configureGateway: options =>
    {
        builder.Configuration.GetSection(ZatcaGatewayOptions.SectionName).Bind(options);
    }
);

var app = builder.Build();

app.UseExceptionHandler();

// Security Headers Middleware
app.Use(async (context, next) =>
{
    context.Response.Headers.Append("X-Content-Type-Options", "nosniff");
    context.Response.Headers.Append("X-Frame-Options", "DENY");
    context.Response.Headers.Append("Referrer-Policy", "strict-origin-when-cross-origin");
    context.Response.Headers.Append("X-XSS-Protection", "0");
    await next();
});

app.UseRateLimiter();
app.UseCors("PlaygroundDevCors");
app.UseDefaultFiles();
app.UseStaticFiles();

// Ensure DB schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
    db.Database.EnsureCreated();
}

// Map Minimal API Endpoints
app.MapInvoiceEndpoints();
app.MapOnboardingEndpoints();

app.MapFallbackToFile("index.html");

app.Run();

public partial class Program { }

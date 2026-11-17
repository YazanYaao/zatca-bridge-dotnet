using System.Text.Json.Serialization;
using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Http.Json;
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

// CORS for local development with Vite dev server (http://localhost:5173)
builder.Services.AddCors(options =>
{
    options.AddPolicy("PlaygroundDevCors", policy =>
    {
        policy.WithOrigins("http://localhost:5173", "http://127.0.0.1:5173")
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

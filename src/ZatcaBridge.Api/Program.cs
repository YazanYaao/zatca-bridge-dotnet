using Microsoft.AspNetCore.Builder;
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

// RFC 7807 Problem Details & Exception Handler
builder.Services.AddProblemDetails();
builder.Services.AddExceptionHandler<ProblemDetailsExceptionHandler>();

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

// Ensure DB schema is created
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
    db.Database.EnsureCreated();
}

// Map Minimal API Endpoints
app.MapInvoiceEndpoints();

app.Run();

public partial class Program { }

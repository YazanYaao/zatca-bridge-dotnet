using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZatcaBridge.Api.Endpoints;
using ZatcaBridge.Application.BackgroundWorkers;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Persistence;

namespace ZatcaBridge.UnitTests.Api;

public class OnboardingEndpointsTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web)
    {
        Converters = { new System.Text.Json.Serialization.JsonStringEnumConverter() }
    };
    private sealed class FakeOnboardingClient : IZatcaOnboardingClient
    {
        public Task<ComplianceCsidResult> RequestComplianceCsidAsync(
            string csrPem,
            string privateKeyPem,
            string otp,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ComplianceCsidResult(
                CredentialId: Guid.NewGuid(),
                RequestId: "req-12345",
                DispositionMessage: "ISSUED",
                Environment: CsidEnvironment.Simulation,
                IsActive: true
            ));
        }

        public Task<ZatcaComplianceCheckResult> SubmitComplianceInvoiceAsync(
            string invoiceHash,
            Guid uuid,
            string signedXml,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ZatcaComplianceCheckResult(
                IsSuccess: true,
                Status: "PASS",
                ReportingStatus: "REPORTED",
                ClearanceStatus: null,
                ErrorMessages: Array.Empty<string>(),
                WarningMessages: Array.Empty<string>(),
                InfoMessages: new[] { "Invoice compliant" }
            ));
        }

        public Task<ProductionCsidResult> RequestProductionCsidAsync(
            string? complianceRequestId = null,
            CancellationToken cancellationToken = default)
        {
            return Task.FromResult(new ProductionCsidResult(
                CredentialId: Guid.NewGuid(),
                RequestId: "prod-req-99999",
                DispositionMessage: "ISSUED",
                Environment: CsidEnvironment.Production,
                IsActive: true
            ));
        }
    }

    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public OnboardingEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _dbName = $"zatca_onboarding_test_{Guid.NewGuid():N}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                services.Configure<OutboxWorkerOptions>(opt => opt.IsEnabled = false);

                // Replace db context
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ZatcaDbContext>));
                if (dbContextDescriptor != null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<ZatcaDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_dbName}");
                });

                // Replace IZatcaOnboardingClient with test fake
                var clientDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IZatcaOnboardingClient));
                if (clientDescriptor != null)
                {
                    services.Remove(clientDescriptor);
                }
                services.AddSingleton<IZatcaOnboardingClient, FakeOnboardingClient>();

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
                db.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    public void Dispose()
    {
        _client.Dispose();
        if (File.Exists(_dbName))
        {
            try { File.Delete(_dbName); } catch { /* best effort */ }
        }
    }

    [Fact]
    public async Task GetStatus_WhenNoCsidOnboarded_ReturnsInactive()
    {
        // Act
        var response = await _client.GetAsync("/api/v1/onboarding/status");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<OnboardingStatusResponse>();
        Assert.NotNull(body);
        Assert.False(body.IsActive);
        Assert.Equal("No active CSID onboarded.", body.Message);
    }

    [Fact]
    public async Task RequestComplianceCsid_MissingOtp_Returns400ProblemDetails()
    {
        // Arrange
        var request = new RequestComplianceCsidApiRequest(
            Otp: "",
            CommonName: "TST-310123456700003",
            OrganizationUnit: "310123456700003",
            OrganizationName: "Acme Saudi Co"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/onboarding/compliance-csid", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("Invalid Argument", problem.Title);
        Assert.Contains("OTP is required", problem.Detail);
    }

    [Fact]
    public async Task RequestComplianceCsid_ValidPayload_Returns200WithResult()
    {
        // Arrange
        var request = new RequestComplianceCsidApiRequest(
            Otp: "123345",
            CommonName: "TST-310123456700003",
            OrganizationUnit: "310123456700003",
            OrganizationName: "Acme Saudi Co",
            CountryCode: "SA",
            Location: "Riyadh",
            Industry: "IT"
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/onboarding/compliance-csid", request);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ComplianceCsidResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("req-12345", result.RequestId);
        Assert.Equal("ISSUED", result.DispositionMessage);
        Assert.Equal(CsidEnvironment.Simulation, result.Environment);
        Assert.True(result.IsActive);
    }

    [Fact]
    public async Task ComplianceCheck_WhenNoCsidActive_Returns400ProblemDetails()
    {
        // Act (db is fresh, no active CSID)
        var response = await _client.PostAsJsonAsync("/api/v1/onboarding/compliance-check", new ComplianceCheckApiRequest());

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(400, problem.Status);
        Assert.Equal("No Active CSID", problem.Title);
    }

    [Fact]
    public async Task RequestProductionCsid_PromotesCsid_Returns200WithResult()
    {
        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/onboarding/production-csid", new RequestProductionCsidApiRequest("req-12345"));

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<ProductionCsidResult>(JsonOptions);
        Assert.NotNull(result);
        Assert.Equal("prod-req-99999", result.RequestId);
        Assert.Equal(CsidEnvironment.Production, result.Environment);
        Assert.True(result.IsActive);
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZatcaBridge.Api.Endpoints;
using ZatcaBridge.Application.BackgroundWorkers;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Persistence;

namespace ZatcaBridge.UnitTests.Api;

public class InvoiceQueryEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public InvoiceQueryEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _dbName = $"zatca_query_test_{Guid.NewGuid():N}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                services.Configure<OutboxWorkerOptions>(opt => opt.IsEnabled = false);

                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ZatcaDbContext>));
                if (dbContextDescriptor != null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<ZatcaDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_dbName}");
                });

                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
                db.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    private static IssueInvoiceRequest CreateSampleInvoiceRequest(string invoiceNumber)
    {
        return new IssueInvoiceRequest(
            InvoiceNumber: invoiceNumber,
            IssueDate: new DateOnly(2026, 11, 18),
            IssueTime: new TimeOnly(12, 0, 0),
            InvoiceType: InvoiceTypeCode.TaxInvoice,
            TransactionType: InvoiceTransactionType.B2B,
            Seller: new PartyDto(
                Name: "Acme Saudi Co",
                VatRegistrationNumber: "310123456700003",
                Street: "Olaya St",
                BuildingNumber: "1234",
                PostalZone: "12211",
                City: "Riyadh",
                District: "Olaya",
                CountryCode: "SA"
            ),
            Buyer: new PartyDto(
                Name: "Buyer Corp",
                VatRegistrationNumber: "310987654300003",
                Street: "King Fahd Rd",
                BuildingNumber: "5678",
                PostalZone: "12334",
                City: "Riyadh",
                District: "Nakheel",
                CountryCode: "SA"
            ),
            LineItems: new List<InvoiceLineItemDto>
            {
                new("Test Product", 2.00m, 100.00m, 0.00m, TaxCategory.Standard)
            }
        );
    }

    [Fact]
    public async Task GetInvoices_EmptyDatabase_ReturnsEmptyPaginatedList()
    {
        var response = await _client.GetAsync("/api/v1/invoices");

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal(0, json.GetProperty("totalCount").GetInt32());
        Assert.Equal(0, json.GetProperty("items").GetArrayLength());
    }

    [Fact]
    public async Task GetInvoices_AfterIssuingInvoices_ReturnsPaginatedList()
    {
        // Arrange
        var issue1 = await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-01"));
        Assert.Equal(HttpStatusCode.Created, issue1.StatusCode);

        var issue2 = await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-02"));
        Assert.Equal(HttpStatusCode.Created, issue2.StatusCode);

        // Act
        var response = await _client.GetAsync("/api/v1/invoices?page=1&pageSize=10");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("totalCount").GetInt32() >= 2);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2);

        var first = items[0];
        Assert.True(first.TryGetProperty("invoiceNumber", out _));
        Assert.True(first.TryGetProperty("payableAmount", out _));
        Assert.True(first.TryGetProperty("icv", out _));
    }

    [Fact]
    public async Task GetInvoiceById_ExistingInvoice_ReturnsFullDetailsWithXmlAndQr()
    {
        // Arrange
        var issueResponse = await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-DETAIL-01"));
        Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);
        var issued = await issueResponse.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(issued);

        // Act
        var response = await _client.GetAsync($"/api/v1/invoices/{issued.InvoiceId}");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.Equal("INV-Q-DETAIL-01", json.GetProperty("invoiceNumber").GetString());
        Assert.Equal(issued.InvoiceId.ToString(), json.GetProperty("id").GetString());
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("xmlPayload").GetString()));
        Assert.False(string.IsNullOrWhiteSpace(json.GetProperty("qrCodeBase64").GetString()));
        Assert.Equal(issued.InvoiceHash, json.GetProperty("invoiceHash").GetString());
    }

    [Fact]
    public async Task GetInvoiceById_NotFound_Returns404ProblemDetails()
    {
        var response = await _client.GetAsync($"/api/v1/invoices/{Guid.NewGuid()}");

        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
        var problem = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problem);
        Assert.Equal(404, problem.Status);
    }

    [Fact]
    public async Task GetOutbox_ReturnsOutboxMessagesList()
    {
        // Arrange
        var issueResponse = await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-OUTBOX-01"));
        Assert.Equal(HttpStatusCode.Created, issueResponse.StatusCode);

        // Act
        var response = await _client.GetAsync("/api/v1/outbox");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        Assert.True(json.GetProperty("totalCount").GetInt32() >= 1);
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 1);
        var first = items[0];
        Assert.True(first.TryGetProperty("status", out _));
        Assert.True(first.TryGetProperty("invoiceHash", out _));
    }

    [Fact]
    public async Task GetChainHistory_ReturnsAllChainRecords()
    {
        // Arrange
        await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-CHAIN-01"));
        await _client.PostAsJsonAsync("/api/v1/invoices", CreateSampleInvoiceRequest("INV-Q-CHAIN-02"));

        // Act
        var response = await _client.GetAsync("/api/v1/invoices/chain");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var json = await response.Content.ReadFromJsonAsync<JsonElement>();
        var items = json.GetProperty("items");
        Assert.True(items.GetArrayLength() >= 2);
        var first = items[0];
        Assert.Equal(1, first.GetProperty("icv").GetInt64());
    }
}

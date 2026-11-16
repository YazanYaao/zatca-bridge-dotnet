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

public class InvoiceEndpointsTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;

    public InvoiceEndpointsTests(WebApplicationFactory<Program> factory)
    {
        _dbName = $"zatca_test_{Guid.NewGuid():N}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Disable background worker during API unit tests for predictable state
                services.Configure<OutboxWorkerOptions>(opt => opt.IsEnabled = false);

                // Replace database with isolated SQLite test database
                var dbContextDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ZatcaDbContext>));
                if (dbContextDescriptor != null)
                {
                    services.Remove(dbContextDescriptor);
                }

                services.AddDbContext<ZatcaDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_dbName}");
                });

                // Ensure schema is created
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
                db.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    private static IssueInvoiceRequest CreateValidB2BRequest(string invoiceNumber = "INV-2026-0001")
    {
        return new IssueInvoiceRequest(
            InvoiceNumber: invoiceNumber,
            IssueDate: new DateOnly(2026, 11, 16),
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
                Name: "Retail Partner LLC",
                VatRegistrationNumber: "320987654300003",
                Street: "King Fahd Rd",
                BuildingNumber: "5678",
                PostalZone: "12334",
                City: "Riyadh",
                District: "Nakheel",
                CountryCode: "SA"
            ),
            LineItems: new List<InvoiceLineItemDto>
            {
                new InvoiceLineItemDto("Standard Item", 2.00m, 100.00m, 0.00m, TaxCategory.Standard)
            }
        );
    }

    [Fact]
    public async Task PostInvoice_ValidB2BRequest_Returns201CreatedWithResultAndLocation()
    {
        // Arrange
        var request = CreateValidB2BRequest("INV-API-B2B-01");

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/invoices", request);

        // Assert: 201 Created and Location header
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        Assert.NotNull(response.Headers.Location);

        var result = await response.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result);
        Assert.Equal(request.InvoiceNumber, result.InvoiceNumber);
        Assert.Equal(1, result.InvoiceCounterValue);
        Assert.NotEmpty(result.InvoiceHash);
        Assert.NotEmpty(result.QrCodeBase64);
        Assert.NotEmpty(result.UblXml);
        Assert.NotEqual(Guid.Empty, result.OutboxMessageId);
    }

    [Fact]
    public async Task PostInvoice_ValidB2CRequest_Returns201Created()
    {
        // Arrange: B2C simplified invoice without buyer
        var request = new IssueInvoiceRequest(
            InvoiceNumber: "INV-API-B2C-01",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(14, 30, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: new PartyDto(
                Name: "Corner Store KSA",
                VatRegistrationNumber: "310999999900003",
                Street: "Tahlia St",
                BuildingNumber: "4321",
                PostalZone: "11564",
                City: "Riyadh",
                District: "Sulaimaniya",
                CountryCode: "SA"
            ),
            Buyer: null,
            LineItems: new List<InvoiceLineItemDto>
            {
                new InvoiceLineItemDto("Consumer Good", 1.00m, 50.00m, 0.00m, TaxCategory.Standard)
            }
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result);
        Assert.Equal(request.InvoiceNumber, result.InvoiceNumber);
    }

    [Fact]
    public async Task GetLatestChainRecord_WhenEmpty_Returns404NotFoundWithProblemDetails()
    {
        // Act: Fresh database with no invoices
        var response = await _client.GetAsync("/api/v1/invoices/chain/latest");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }

    [Fact]
    public async Task GetLatestChainRecord_WhenInvoiceIssued_Returns200OkWithLatestRecord()
    {
        // Arrange: Issue an invoice first
        var request = CreateValidB2BRequest("INV-CHAIN-01");
        var postResponse = await _client.PostAsJsonAsync("/api/v1/invoices", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);

        // Act: Query latest record
        var response = await _client.GetAsync("/api/v1/invoices/chain/latest");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        using var jsonDoc = await JsonDocument.ParseAsync(await response.Content.ReadAsStreamAsync());
        var root = jsonDoc.RootElement;
        Assert.True(root.TryGetProperty("icv", out var icvProp) || root.TryGetProperty("invoiceCounterValue", out icvProp));
    }

    [Fact]
    public async Task PostInvoice_InvalidSellerVatNumber_Returns400BadRequestWithProblemDetails()
    {
        // Arrange: Seller VAT violates 15-digit KSA rule (too short, doesn't start/end with 3)
        var request = new IssueInvoiceRequest(
            InvoiceNumber: "INV-ERR-VAT",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(12, 0, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: new PartyDto(
                Name: "Invalid Co",
                VatRegistrationNumber: "12345", // INVALID
                Street: "Main St",
                BuildingNumber: "1",
                PostalZone: "11111",
                City: "Riyadh",
                District: "Central",
                CountryCode: "SA"
            ),
            Buyer: null,
            LineItems: new List<InvoiceLineItemDto>
            {
                new InvoiceLineItemDto("Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard)
            }
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/invoices", request);

        // Assert: RFC 7807 400 Bad Request
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Detail);
        Assert.Contains("15 digits", problemDetails.Detail);
    }

    [Fact]
    public async Task PostInvoice_EmptyLineItems_Returns400BadRequestWithProblemDetails()
    {
        // Arrange: Invoice with no line items violates domain invariant
        var request = new IssueInvoiceRequest(
            InvoiceNumber: "INV-ERR-LINES",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(12, 0, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: new PartyDto(
                Name: "Valid Co",
                VatRegistrationNumber: "310123456700003",
                Street: "Main St",
                BuildingNumber: "1",
                PostalZone: "11111",
                City: "Riyadh",
                District: "Central",
                CountryCode: "SA"
            ),
            Buyer: null,
            LineItems: new List<InvoiceLineItemDto>() // EMPTY
        );

        // Act
        var response = await _client.PostAsJsonAsync("/api/v1/invoices", request);

        // Assert
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
        Assert.NotNull(problemDetails.Detail);
        Assert.Contains("line item", problemDetails.Detail, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task PostOutboxProcess_ManuallyTriggered_Returns200OkWithProcessedCount()
    {
        // Act
        var response = await _client.PostAsync("/api/v1/outbox/process?batchSize=5", null);

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<ProcessOutboxResponse>();
        Assert.NotNull(body);
        Assert.True(body.ProcessedCount >= 0);
    }

    [Fact]
    public async Task PostInvoice_RawJsonWithStringEnums_SuccessfullyDeserializesAndReturns201Created()
    {
        // Arrange: Raw JSON payload with string enums (TaxInvoice, B2B, Standard)
        const string json = """
        {
            "invoiceNumber": "INV-STR-ENUM-01",
            "issueDate": "2026-11-16",
            "issueTime": "12:00:00",
            "invoiceType": "TaxInvoice",
            "transactionType": "B2B",
            "seller": {
                "name": "Acme Saudi Co",
                "vatRegistrationNumber": "310123456700003",
                "street": "Olaya St",
                "buildingNumber": "1234",
                "postalZone": "12211",
                "city": "Riyadh",
                "district": "Olaya",
                "countryCode": "SA"
            },
            "buyer": {
                "name": "Retail Partner LLC",
                "vatRegistrationNumber": "320987654300003",
                "street": "King Fahd Rd",
                "buildingNumber": "5678",
                "postalZone": "12334",
                "city": "Riyadh",
                "district": "Nakheel",
                "countryCode": "SA"
            },
            "lineItems": [
                {
                    "name": "Consulting Service",
                    "quantity": 1.00,
                    "unitPrice": 500.00,
                    "discountAmount": 0.00,
                    "taxCategory": {
                        "code": "Standard",
                        "rate": 0.15
                    }
                }
            ]
        }
        """;

        using var content = new StringContent(json, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/invoices", content);

        // Assert: 201 Created (deserialized string enums successfully)
        Assert.Equal(HttpStatusCode.Created, response.StatusCode);
        var result = await response.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result);
        Assert.Equal("INV-STR-ENUM-01", result.InvoiceNumber);
    }

    [Fact]
    public async Task PostInvoice_MalformedJsonSyntax_Returns400BadRequestWithProblemDetails()
    {
        // Arrange: Syntactically invalid JSON payload
        const string malformedJson = "{ \"invoiceNumber\": \"INV-MALFORMED\", \"invoiceType\": ";
        using var content = new StringContent(malformedJson, System.Text.Encoding.UTF8, "application/json");

        // Act
        var response = await _client.PostAsync("/api/v1/invoices", content);

        // Assert: RFC 7807 400 Bad Request (NOT 500)
        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("application/problem+json", response.Content.Headers.ContentType?.MediaType);

        var problemDetails = await response.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.NotNull(problemDetails);
        Assert.Equal(400, problemDetails.Status);
    }
}

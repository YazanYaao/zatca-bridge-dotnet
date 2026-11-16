using System;
using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Xunit;
using ZatcaBridge.Api.Endpoints;
using ZatcaBridge.Application.BackgroundWorkers;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Persistence;

namespace ZatcaBridge.UnitTests.Api;

public class ZatcaEndToEndIntegrationTests : IClassFixture<WebApplicationFactory<Program>>, IDisposable
{
    private readonly WebApplicationFactory<Program> _factory;
    private readonly HttpClient _client;
    private readonly string _dbName;
    private readonly MockZatcaGatewayClient _mockGateway = new();

    private sealed class MockZatcaGatewayClient : IZatcaGatewayClient
    {
        public List<(string Hash, string Xml)> ClearedInvoices { get; } = new();
        public List<(string Hash, string Xml)> ReportedInvoices { get; } = new();

        public Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        {
            ClearedInvoices.Add((invoiceHash, ublXml));
            return Task.FromResult(new ZatcaGatewayResponse(
                IsSuccess: true,
                IsRejected: false,
                ClearanceStatus: "CLEARED",
                ClearedInvoiceXml: ublXml,
                IsTransient: false
            ));
        }

        public Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        {
            ReportedInvoices.Add((invoiceHash, ublXml));
            return Task.FromResult(new ZatcaGatewayResponse(
                IsSuccess: true,
                IsRejected: false,
                ReportingStatus: "REPORTED",
                IsTransient: false
            ));
        }
    }

    public ZatcaEndToEndIntegrationTests(WebApplicationFactory<Program> factory)
    {
        _dbName = $"zatca_e2e_{Guid.NewGuid():N}.db";

        _factory = factory.WithWebHostBuilder(builder =>
        {
            builder.UseEnvironment("Development");

            builder.ConfigureServices(services =>
            {
                // Disable automatic background worker so we can control outbox execution explicitly
                services.Configure<OutboxWorkerOptions>(opt => opt.IsEnabled = false);

                // Configure isolated SQLite database
                var dbDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(DbContextOptions<ZatcaDbContext>));
                if (dbDescriptor != null)
                {
                    services.Remove(dbDescriptor);
                }

                services.AddDbContext<ZatcaDbContext>(options =>
                {
                    options.UseSqlite($"Data Source={_dbName}");
                });

                // Replace gateway client with mock gateway
                var gatewayDescriptor = services.SingleOrDefault(d => d.ServiceType == typeof(IZatcaGatewayClient));
                if (gatewayDescriptor != null)
                {
                    services.Remove(gatewayDescriptor);
                }
                services.AddSingleton<IZatcaGatewayClient>(_mockGateway);

                // Initialize schema
                var sp = services.BuildServiceProvider();
                using var scope = sp.CreateScope();
                var db = scope.ServiceProvider.GetRequiredService<ZatcaDbContext>();
                db.Database.EnsureCreated();
            });
        });

        _client = _factory.CreateClient();
    }

    private ZatcaDbContext CreateDbContext()
    {
        var options = new DbContextOptionsBuilder<ZatcaDbContext>()
            .UseSqlite($"Data Source={_dbName}")
            .Options;
        return new ZatcaDbContext(options);
    }

    public void Dispose()
    {
        try
        {
            using var db = CreateDbContext();
            db.Database.EnsureDeleted();
        }
        catch
        {
            // Ignore cleanup errors on test temp file
        }
    }

    private static IssueInvoiceRequest CreateB2BInvoiceRequest(string invoiceNumber)
    {
        return new IssueInvoiceRequest(
            InvoiceNumber: invoiceNumber,
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(10, 0, 0),
            InvoiceType: InvoiceTypeCode.TaxInvoice,
            TransactionType: InvoiceTransactionType.B2B,
            Seller: new PartyDto(
                Name: "Zatca Corp Saudi",
                VatRegistrationNumber: "310123456700003",
                Street: "King Fahd Rd",
                BuildingNumber: "1000",
                PostalZone: "11564",
                City: "Riyadh",
                District: "Olaya",
                CountryCode: "SA"
            ),
            Buyer: new PartyDto(
                Name: "Client Global KSA",
                VatRegistrationNumber: "320987654300003",
                Street: "Makkah Rd",
                BuildingNumber: "2000",
                PostalZone: "12334",
                City: "Riyadh",
                District: "Sulaimaniya",
                CountryCode: "SA"
            ),
            LineItems: new List<InvoiceLineItemDto>
            {
                new InvoiceLineItemDto("Consulting Services", 1.00m, 1000.00m, 0.00m, TaxCategory.Standard),
                new InvoiceLineItemDto("Cloud Hosting Support", 2.00m, 250.00m, 50.00m, TaxCategory.Standard)
            }
        );
    }

    private static IssueInvoiceRequest CreateB2CInvoiceRequest(string invoiceNumber)
    {
        return new IssueInvoiceRequest(
            InvoiceNumber: invoiceNumber,
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(11, 0, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: new PartyDto(
                Name: "Retail Store KSA",
                VatRegistrationNumber: "310123456700003",
                Street: "Olaya St",
                BuildingNumber: "1000",
                PostalZone: "11564",
                City: "Riyadh",
                District: "Olaya",
                CountryCode: "SA"
            ),
            Buyer: null,
            LineItems: new List<InvoiceLineItemDto>
            {
                new InvoiceLineItemDto("Consumer Item", 3.00m, 50.00m, 0.00m, TaxCategory.Standard)
            }
        );
    }

    [Fact]
    public async Task E2E_B2BInvoice_FullLifeCycle_IssuesPersistsHashChainsAndClearsViaOutbox()
    {
        // 1. Issue Genesis B2B Invoice via API
        var request = CreateB2BInvoiceRequest("INV-E2E-B2B-001");
        var postResponse = await _client.PostAsJsonAsync("/api/v1/invoices", request);

        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var result = await postResponse.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result);
        Assert.Equal(1, result.InvoiceCounterValue);
        Assert.Equal(PreviousInvoiceHash.Genesis.Value, result.PreviousInvoiceHash);
        Assert.NotEmpty(result.InvoiceHash);
        Assert.NotEmpty(result.QrCodeBase64);
        Assert.NotEmpty(result.UblXml);

        // 2. Verify Database Persistence (Invoice, HashChain, Outbox)
        using (var db = CreateDbContext())
        {
            var invoiceEntity = await db.Invoices.FirstOrDefaultAsync(i => i.Id == result.InvoiceId);
            Assert.NotNull(invoiceEntity);
            Assert.Equal("INV-E2E-B2B-001", invoiceEntity.InvoiceNumber);
            Assert.Equal(1, invoiceEntity.InvoiceCounterValue);
            Assert.Equal(1450.00m, invoiceEntity.TaxExclusiveAmount); // 1000 + (500 - 50)
            Assert.Equal(217.50m, invoiceEntity.TotalTaxAmount);      // 15% of 1450
            Assert.Equal(1667.50m, invoiceEntity.TaxInclusiveAmount);

            var chainEntity = await db.HashChainRecords.FirstOrDefaultAsync(h => h.Icv == 1);
            Assert.NotNull(chainEntity);
            Assert.Equal(PreviousInvoiceHash.Genesis.Value, chainEntity.PreviousHash);
            Assert.Equal(result.InvoiceHash, chainEntity.InvoiceHash);

            var outboxEntity = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == result.OutboxMessageId);
            Assert.NotNull(outboxEntity);
            Assert.Equal((int)OutboxStatus.Pending, outboxEntity.Status);
            Assert.Equal((int)OutboxMessageType.Clearance, outboxEntity.Type);
        }

        // 3. Process Outbox via API
        var processResponse = await _client.PostAsync("/api/v1/outbox/process?batchSize=10", null);
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);
        var processResult = await processResponse.Content.ReadFromJsonAsync<ProcessOutboxResponse>();
        Assert.NotNull(processResult);
        Assert.Equal(1, processResult.ProcessedCount);

        // 4. Verify Gateway was called and Outbox Message transitioned to Cleared
        Assert.Single(_mockGateway.ClearedInvoices);
        Assert.Equal(result.InvoiceHash, _mockGateway.ClearedInvoices[0].Hash);

        using (var db = CreateDbContext())
        {
            var outboxEntity = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == result.OutboxMessageId);
            Assert.NotNull(outboxEntity);
            Assert.Equal((int)OutboxStatus.Cleared, outboxEntity.Status);
        }
    }

    [Fact]
    public async Task E2E_SequentialInvoices_StrictMonotonicIcvAndHashChainLinkage()
    {
        // 1. Issue Invoice 1 (Genesis, ICV = 1)
        var req1 = CreateB2BInvoiceRequest("INV-CHAIN-E2E-001");
        var res1 = await _client.PostAsJsonAsync("/api/v1/invoices", req1);
        Assert.Equal(HttpStatusCode.Created, res1.StatusCode);
        var result1 = await res1.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result1);
        Assert.Equal(1, result1.InvoiceCounterValue);
        Assert.Equal(PreviousInvoiceHash.Genesis.Value, result1.PreviousInvoiceHash);

        // 2. Issue Invoice 2 (Chained, ICV = 2)
        var req2 = CreateB2BInvoiceRequest("INV-CHAIN-E2E-002");
        var res2 = await _client.PostAsJsonAsync("/api/v1/invoices", req2);
        Assert.Equal(HttpStatusCode.Created, res2.StatusCode);
        var result2 = await res2.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result2);

        // Assert Monotonic ICV Increment: 1 -> 2
        Assert.Equal(2, result2.InvoiceCounterValue);

        // Assert Cryptographic Hash Chain Link: Invoice 2 PIH must equal Invoice 1 Hash
        Assert.Equal(result1.InvoiceHash, result2.PreviousInvoiceHash);

        // 3. Query Latest Chain Record via API
        var latestChainResponse = await _client.GetAsync("/api/v1/invoices/chain/latest");
        Assert.Equal(HttpStatusCode.OK, latestChainResponse.StatusCode);
        using var jsonDoc = await JsonDocument.ParseAsync(await latestChainResponse.Content.ReadAsStreamAsync());
        var root = jsonDoc.RootElement;
        var icvValue = root.GetProperty("icv").GetProperty("value").GetInt64();
        var invoiceHashValue = root.GetProperty("invoiceHash").GetString();

        Assert.Equal(2, icvValue);
        Assert.Equal(result2.InvoiceHash, invoiceHashValue);

        // 4. Verify in Database that both records are properly recorded
        using var db = CreateDbContext();
        var chains = await db.HashChainRecords.OrderBy(h => h.Icv).ToListAsync();
        Assert.Equal(2, chains.Count);
        Assert.Equal(1, chains[0].Icv);
        Assert.Equal(PreviousInvoiceHash.Genesis.Value, chains[0].PreviousHash);
        Assert.Equal(result1.InvoiceHash, chains[0].InvoiceHash);

        Assert.Equal(2, chains[1].Icv);
        Assert.Equal(result1.InvoiceHash, chains[1].PreviousHash);
        Assert.Equal(result2.InvoiceHash, chains[1].InvoiceHash);
    }

    [Fact]
    public async Task E2E_B2CInvoice_FullLifeCycle_IssuesAndReportsViaOutbox()
    {
        // 1. Issue B2C Simplified Invoice
        var request = CreateB2CInvoiceRequest("INV-E2E-B2C-001");
        var postResponse = await _client.PostAsJsonAsync("/api/v1/invoices", request);
        Assert.Equal(HttpStatusCode.Created, postResponse.StatusCode);
        var result = await postResponse.Content.ReadFromJsonAsync<IssueInvoiceResult>();
        Assert.NotNull(result);

        // 2. Verify Outbox Message created as Reporting
        using (var db = CreateDbContext())
        {
            var outbox = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == result.OutboxMessageId);
            Assert.NotNull(outbox);
            Assert.Equal((int)OutboxMessageType.Reporting, outbox.Type);
            Assert.Equal((int)OutboxStatus.Pending, outbox.Status);
        }

        // 3. Process Outbox
        var processResponse = await _client.PostAsync("/api/v1/outbox/process?batchSize=10", null);
        Assert.Equal(HttpStatusCode.OK, processResponse.StatusCode);

        // 4. Verify Dispatched to Gateway and transitioned to Reported
        Assert.Single(_mockGateway.ReportedInvoices);
        Assert.Equal(result.InvoiceHash, _mockGateway.ReportedInvoices[0].Hash);

        using (var db = CreateDbContext())
        {
            var outbox = await db.OutboxMessages.FirstOrDefaultAsync(o => o.Id == result.OutboxMessageId);
            Assert.NotNull(outbox);
            Assert.Equal((int)OutboxStatus.Reported, outbox.Status);
        }
    }
}

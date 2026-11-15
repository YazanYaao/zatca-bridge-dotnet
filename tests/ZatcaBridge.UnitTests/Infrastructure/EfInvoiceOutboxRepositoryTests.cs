using System;
using System.Linq;
using System.Threading.Tasks;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Xunit;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Persistence;
using ZatcaBridge.Infrastructure.Persistence.Entities;
using ZatcaBridge.Infrastructure.Persistence.Repositories;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class EfInvoiceOutboxRepositoryTests : IDisposable
{
    private readonly SqliteConnection _connection;
    private readonly DbContextOptions<ZatcaDbContext> _options;

    public EfInvoiceOutboxRepositoryTests()
    {
        _connection = new SqliteConnection("DataSource=:memory:");
        _connection.Open();

        _options = new DbContextOptionsBuilder<ZatcaDbContext>()
            .UseSqlite(_connection)
            .Options;

        using var context = new ZatcaDbContext(_options);
        context.Database.EnsureCreated();
    }

    public void Dispose()
    {
        _connection.Dispose();
    }

    private ZatcaDbContext CreateContext() => new(_options);

    private static Party CreateSeller() =>
        new Party("Acme Saudi Co", "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA");

    private static Party CreateBuyer() =>
        new Party("Retail Partner LLC", "320987654300003", "King Fahd Rd", "5678", "12334", "Riyadh", "Nakheel", "SA");

    private static Invoice CreateSampleInvoice(long icv, string? pih = null)
    {
        var line = new InvoiceLineItem(Guid.NewGuid(), "Cloud Server", 1.00m, 500.00m, 0.00m, TaxCategory.Standard);
        return new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            $"INV-2026-{icv:D4}",
            new DateOnly(2026, 11, 16),
            new TimeOnly(12, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { line },
            invoiceCounterValue: icv,
            previousInvoiceHash: pih ?? PreviousInvoiceHash.Genesis.Value
        );
    }

    [Fact]
    public async Task GetLatestRecordAsync_EmptyDatabase_ReturnsNull()
    {
        // Arrange
        await using var context = CreateContext();
        var repo = new EfInvoiceOutboxRepository(context);

        // Act
        var latest = await repo.GetLatestRecordAsync();

        // Assert
        Assert.Null(latest);
    }

    [Fact]
    public async Task GetLatestRecordAsync_MultipleRecords_ReturnsRecordWithHighestIcv()
    {
        // Arrange
        await using (var context = CreateContext())
        {
            context.HashChainRecords.AddRange(
                new HashChainEntity { Icv = 1, PreviousHash = PreviousInvoiceHash.Genesis.Value, InvoiceHash = "aGFzaC0x", CreatedAt = DateTimeOffset.UtcNow },
                new HashChainEntity { Icv = 3, PreviousHash = "aGFzaC0y", InvoiceHash = "aGFzaC0z", CreatedAt = DateTimeOffset.UtcNow },
                new HashChainEntity { Icv = 2, PreviousHash = "aGFzaC0x", InvoiceHash = "aGFzaC0y", CreatedAt = DateTimeOffset.UtcNow }
            );
            await context.SaveChangesAsync();
        }

        // Act
        await using var readContext = CreateContext();
        var repo = new EfInvoiceOutboxRepository(readContext);
        var latest = await repo.GetLatestRecordAsync();

        // Assert
        Assert.NotNull(latest);
        Assert.Equal(3, latest.Icv.Value);
        Assert.Equal("aGFzaC0y", latest.PreviousHash.Value);
        Assert.Equal("aGFzaC0z", latest.InvoiceHash);
    }

    [Fact]
    public async Task SaveInvoiceWithOutboxAsync_ValidInputs_PersistsAllEntitiesAtomically()
    {
        // Arrange
        var invoice = CreateSampleInvoice(1);
        var chainRecord = HashChainRecord.CreateGenesis("computed-invoice-hash-01");
        var outboxMessage = new OutboxMessage(
            Guid.NewGuid(),
            invoice.Id,
            OutboxMessageType.Clearance,
            "<Invoice>Test</Invoice>",
            "computed-invoice-hash-01",
            DateTimeOffset.UtcNow
        );

        // Act
        await using (var context = CreateContext())
        {
            var repo = new EfInvoiceOutboxRepository(context);
            await repo.SaveInvoiceWithOutboxAsync(invoice, chainRecord, outboxMessage);
        }

        // Assert
        await using (var verifyContext = CreateContext())
        {
            var savedInvoice = await verifyContext.Invoices.FirstOrDefaultAsync(i => i.Id == invoice.Id);
            Assert.NotNull(savedInvoice);
            Assert.Equal(invoice.InvoiceNumber, savedInvoice.InvoiceNumber);
            Assert.Equal(invoice.MonetaryTotal.PayableAmount, savedInvoice.PayableAmount);
            Assert.Equal(invoice.TotalTaxAmount, savedInvoice.TotalTaxAmount);

            var savedChain = await verifyContext.HashChainRecords.FirstOrDefaultAsync(c => c.Icv == 1);
            Assert.NotNull(savedChain);
            Assert.Equal(PreviousInvoiceHash.Genesis.Value, savedChain.PreviousHash);
            Assert.Equal("computed-invoice-hash-01", savedChain.InvoiceHash);

            var savedOutbox = await verifyContext.OutboxMessages.FirstOrDefaultAsync(o => o.Id == outboxMessage.Id);
            Assert.NotNull(savedOutbox);
            Assert.Equal(invoice.Id, savedOutbox.InvoiceId);
            Assert.Equal(OutboxStatus.Pending, (OutboxStatus)savedOutbox.Status);
            Assert.Equal(outboxMessage.PayloadXml, savedOutbox.PayloadXml);
        }
    }

    [Fact]
    public async Task SaveInvoiceWithOutboxAsync_DuplicateIcv_ThrowsDbUpdateExceptionDueToUniqueConstraint()
    {
        // Arrange: Insert record with ICV = 1
        var invoice1 = CreateSampleInvoice(1);
        var chain1 = HashChainRecord.CreateGenesis("hash-1");
        var outbox1 = new OutboxMessage(Guid.NewGuid(), invoice1.Id, OutboxMessageType.Clearance, "<xml/>", "hash-1", DateTimeOffset.UtcNow);

        await using (var context = CreateContext())
        {
            var repo = new EfInvoiceOutboxRepository(context);
            await repo.SaveInvoiceWithOutboxAsync(invoice1, chain1, outbox1);
        }

        // Act & Assert: Attempting to insert another record with ICV = 1 must fail unique constraint
        var invoice2 = CreateSampleInvoice(1); // Same ICV!
        var chain2 = HashChainRecord.CreateGenesis("hash-2");
        var outbox2 = new OutboxMessage(Guid.NewGuid(), invoice2.Id, OutboxMessageType.Clearance, "<xml2/>", "hash-2", DateTimeOffset.UtcNow);

        await using (var context2 = CreateContext())
        {
            var repo2 = new EfInvoiceOutboxRepository(context2);
            await Assert.ThrowsAsync<DbUpdateException>(() =>
                repo2.SaveInvoiceWithOutboxAsync(invoice2, chain2, outbox2));
        }
    }

    [Fact]
    public void DecimalPrecision_MonetaryColumns_ConfiguredWithPrecision18Scale2()
    {
        // Arrange
        using var context = CreateContext();
        var model = context.Model;
        var entity = model.FindEntityType(typeof(InvoiceEntity));
        Assert.NotNull(entity);

        // Assert decimal precision configuration for all monetary properties
        string[] monetaryProperties =
        {
            nameof(InvoiceEntity.LineExtensionAmount),
            nameof(InvoiceEntity.TaxExclusiveAmount),
            nameof(InvoiceEntity.TaxInclusiveAmount),
            nameof(InvoiceEntity.TotalTaxAmount),
            nameof(InvoiceEntity.PayableAmount)
        };

        foreach (var propName in monetaryProperties)
        {
            var property = entity.FindProperty(propName);
            Assert.NotNull(property);
            Assert.Equal(18, property.GetPrecision());
            Assert.Equal(2, property.GetScale());
        }
    }

    [Fact]
    public async Task GetPendingMessagesAsync_ReturnsPendingMessagesOrderedByIcv()
    {
        // Arrange
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();
        var id3 = Guid.NewGuid();

        await using (var context = CreateContext())
        {
            context.OutboxMessages.AddRange(
                new OutboxMessageEntity
                {
                    Id = id2,
                    InvoiceId = Guid.NewGuid(),
                    Type = (int)OutboxMessageType.Clearance,
                    PayloadXml = "<xml-2/>",
                    InvoiceHash = "h2",
                    InvoiceCounterValue = 2,
                    Status = (int)OutboxStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(2)
                },
                new OutboxMessageEntity
                {
                    Id = id3,
                    InvoiceId = Guid.NewGuid(),
                    Type = (int)OutboxMessageType.Clearance,
                    PayloadXml = "<xml-3/>",
                    InvoiceHash = "h3",
                    InvoiceCounterValue = 3,
                    Status = (int)OutboxStatus.Completed, // Not Pending!
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(3)
                },
                new OutboxMessageEntity
                {
                    Id = id1,
                    InvoiceId = Guid.NewGuid(),
                    Type = (int)OutboxMessageType.Clearance,
                    PayloadXml = "<xml-1/>",
                    InvoiceHash = "h1",
                    InvoiceCounterValue = 1,
                    Status = (int)OutboxStatus.Pending,
                    CreatedAt = DateTimeOffset.UtcNow.AddMinutes(1)
                }
            );
            await context.SaveChangesAsync();
        }

        // Act
        await using var readContext = CreateContext();
        var repo = new EfInvoiceOutboxRepository(readContext);
        var pending = await repo.GetPendingMessagesAsync(10);

        // Assert: Should only return pending messages, ordered by ICV (1 then 2)
        Assert.Equal(2, pending.Count);
        Assert.Equal(id1, pending[0].Id);
        Assert.Equal(id2, pending[1].Id);
    }

    [Fact]
    public async Task UpdateOutboxStatusAsync_UpdatesStatusAndErrorMessage()
    {
        // Arrange
        var messageId = Guid.NewGuid();
        await using (var context = CreateContext())
        {
            context.OutboxMessages.Add(new OutboxMessageEntity
            {
                Id = messageId,
                InvoiceId = Guid.NewGuid(),
                Type = (int)OutboxMessageType.Clearance,
                PayloadXml = "<xml/>",
                InvoiceHash = "h",
                InvoiceCounterValue = 1,
                Status = (int)OutboxStatus.Pending,
                CreatedAt = DateTimeOffset.UtcNow
            });
            await context.SaveChangesAsync();
        }

        // Act
        await using (var updateContext = CreateContext())
        {
            var repo = new EfInvoiceOutboxRepository(updateContext);
            await repo.UpdateOutboxStatusAsync(messageId, OutboxStatus.Failed, "ZATCA HTTP 500 error");
        }

        // Assert
        await using (var verifyContext = CreateContext())
        {
            var updated = await verifyContext.OutboxMessages.FirstOrDefaultAsync(m => m.Id == messageId);
            Assert.NotNull(updated);
            Assert.Equal((int)OutboxStatus.Failed, updated.Status);
            Assert.Equal("ZATCA HTTP 500 error", updated.ErrorMessage);
            Assert.Equal(1, updated.RetryCount);
        }
    }
}

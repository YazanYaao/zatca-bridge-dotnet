using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Invoices.Commands;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Application.Xml;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Application;

public class IssueInvoiceHandlerTests
{
    private sealed class FakeHashChainRepository : IHashChainRepository
    {
        public HashChainRecord? LatestRecord { get; set; }

        public Task<HashChainRecord?> GetLatestRecordAsync(CancellationToken cancellationToken = default) =>
            Task.FromResult(LatestRecord);
    }

    private sealed class FakeInvoiceOutboxRepository : IInvoiceOutboxRepository
    {
        public Invoice? SavedInvoice { get; private set; }
        public HashChainRecord? SavedChainRecord { get; private set; }
        public OutboxMessage? SavedOutboxMessage { get; private set; }
        public int SaveCallCount { get; private set; }

        public Task SaveInvoiceWithOutboxAsync(
            Invoice invoice,
            HashChainRecord newChainRecord,
            OutboxMessage outboxMessage,
            CancellationToken cancellationToken = default)
        {
            SavedInvoice = invoice;
            SavedChainRecord = newChainRecord;
            SavedOutboxMessage = outboxMessage;
            SaveCallCount++;
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default) =>
            Task.FromResult<IReadOnlyList<OutboxMessage>>(Array.Empty<OutboxMessage>());

        public Task UpdateOutboxStatusAsync(Guid messageId, OutboxStatus status, string? errorMessage = null, CancellationToken cancellationToken = default) =>
            Task.CompletedTask;
    }

    private static Party CreateSeller() =>
        new Party("Acme Saudi Co", "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA");

    private static Party CreateBuyer() =>
        new Party("Retail Partner LLC", "320987654300003", "King Fahd Rd", "5678", "12334", "Riyadh", "Nakheel", "SA");

    [Fact]
    public async Task HandleAsync_GenesisInvoice_AppliesGenesisIcvAndPihAndQueuesReportingOutbox()
    {
        // Arrange
        var hashChainRepo = new FakeHashChainRepository { LatestRecord = null }; // First invoice in system
        var outboxRepo = new FakeInvoiceOutboxRepository();
        var xmlGenerator = new UblXmlGenerator();
        var handler = new IssueInvoiceHandler(hashChainRepo, outboxRepo, xmlGenerator);

        var command = new IssueInvoiceCommand(
            InvoiceNumber: "INV-B2C-001",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(12, 0, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: CreateSeller(),
            Buyer: null,
            LineItems: new[]
            {
                new InvoiceLineItemDto("Espresso", 1.00m, 15.00m, 0.00m, TaxCategory.Standard)
            }
        );

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.NotNull(result);
        Assert.Equal(1, result.InvoiceCounterValue);
        Assert.Equal(PreviousInvoiceHash.Genesis.Value, result.PreviousInvoiceHash);
        Assert.False(string.IsNullOrWhiteSpace(result.InvoiceHash));
        Assert.False(string.IsNullOrWhiteSpace(result.QrCodeBase64));
        Assert.False(string.IsNullOrWhiteSpace(result.UblXml));

        // Verify outbox repository received the entities
        Assert.Equal(1, outboxRepo.SaveCallCount);
        Assert.NotNull(outboxRepo.SavedInvoice);
        Assert.NotNull(outboxRepo.SavedChainRecord);
        Assert.NotNull(outboxRepo.SavedOutboxMessage);

        Assert.Equal(1, outboxRepo.SavedChainRecord.Icv.Value);
        Assert.Equal(PreviousInvoiceHash.Genesis, outboxRepo.SavedChainRecord.PreviousHash);
        Assert.Equal(result.InvoiceHash, outboxRepo.SavedChainRecord.InvoiceHash);

        // B2C must be queued for Reporting
        Assert.Equal(OutboxMessageType.Reporting, outboxRepo.SavedOutboxMessage.Type);
        Assert.Equal(OutboxStatus.Pending, outboxRepo.SavedOutboxMessage.Status);
        Assert.Equal(result.UblXml, outboxRepo.SavedOutboxMessage.PayloadXml);
        Assert.Equal(result.InvoiceHash, outboxRepo.SavedOutboxMessage.InvoiceHash);
    }

    [Fact]
    public async Task HandleAsync_SubsequentInvoice_IncrementsIcvAndChainsPreviousHashAndQueuesClearanceOutbox()
    {
        // Arrange
        const string previousInvoiceHash = "cHJldmlvdXMtaGFzaC0wMDU=";
        var previousRecord = new HashChainRecord(
            new InvoiceCounterValue(5),
            PreviousInvoiceHash.FromHash("cHJldmlvdXMtaGFzaC0wMDQ="),
            previousInvoiceHash
        );

        var hashChainRepo = new FakeHashChainRepository { LatestRecord = previousRecord };
        var outboxRepo = new FakeInvoiceOutboxRepository();
        var xmlGenerator = new UblXmlGenerator();
        var handler = new IssueInvoiceHandler(hashChainRepo, outboxRepo, xmlGenerator);

        var command = new IssueInvoiceCommand(
            InvoiceNumber: "INV-B2B-006",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(13, 0, 0),
            InvoiceType: InvoiceTypeCode.TaxInvoice,
            TransactionType: InvoiceTransactionType.B2B,
            Seller: CreateSeller(),
            Buyer: CreateBuyer(),
            LineItems: new[]
            {
                new InvoiceLineItemDto("Enterprise License", 2.00m, 5000.00m, 500.00m, TaxCategory.Standard)
            }
        );

        // Act
        var result = await handler.HandleAsync(command);

        // Assert
        Assert.Equal(6, result.InvoiceCounterValue);
        Assert.Equal(previousInvoiceHash, result.PreviousInvoiceHash);

        Assert.Equal(1, outboxRepo.SaveCallCount);
        Assert.Equal(6, outboxRepo.SavedChainRecord!.Icv.Value);
        Assert.Equal(previousInvoiceHash, outboxRepo.SavedChainRecord.PreviousHash.Value);
        Assert.Equal(result.InvoiceHash, outboxRepo.SavedChainRecord.InvoiceHash);

        // B2B must be queued for Clearance
        Assert.Equal(OutboxMessageType.Clearance, outboxRepo.SavedOutboxMessage!.Type);
    }

    [Fact]
    public async Task HandleAsync_EndToEndOrchestration_ProducesValidHashAndQrCode()
    {
        // Arrange
        var hashChainRepo = new FakeHashChainRepository { LatestRecord = null };
        var outboxRepo = new FakeInvoiceOutboxRepository();
        var xmlGenerator = new UblXmlGenerator();
        var handler = new IssueInvoiceHandler(hashChainRepo, outboxRepo, xmlGenerator);

        var command = new IssueInvoiceCommand(
            InvoiceNumber: "INV-2026-TEST",
            IssueDate: new DateOnly(2026, 11, 16),
            IssueTime: new TimeOnly(10, 0, 0),
            InvoiceType: InvoiceTypeCode.SimplifiedTaxInvoice,
            TransactionType: InvoiceTransactionType.B2C,
            Seller: CreateSeller(),
            Buyer: null,
            LineItems: new[]
            {
                new InvoiceLineItemDto("Widget", 2.00m, 50.00m, 0.00m, TaxCategory.Standard)
            }
        );

        // Act
        var result = await handler.HandleAsync(command);

        // Assert: Verify invoice hash matches SHA-256 computation over UBL XML
        var computedHash = InvoiceHashCalculator.ComputeHash(result.UblXml);
        Assert.Equal(computedHash.Base64Hash, result.InvoiceHash);

        // Assert: Verify QR code decodes properly
        var decodedTags = TlvEncoder.Decode(result.QrCodeBase64);
        Assert.True(decodedTags.Count >= 5);
        Assert.Equal("Acme Saudi Co", System.Text.Encoding.UTF8.GetString(decodedTags.First(t => t.TagNumber == 1).Value));
        Assert.Equal("310123456700003", System.Text.Encoding.UTF8.GetString(decodedTags.First(t => t.TagNumber == 2).Value));
    }

    [Fact]
    public async Task HandleAsync_NullCommand_ThrowsArgumentNullException()
    {
        var handler = new IssueInvoiceHandler(
            new FakeHashChainRepository(),
            new FakeInvoiceOutboxRepository(),
            new UblXmlGenerator()
        );

        await Assert.ThrowsAsync<ArgumentNullException>(() => handler.HandleAsync(null!));
    }
}

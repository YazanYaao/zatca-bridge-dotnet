using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Application.Services;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Application;

public class OutboxProcessorTests
{
    private sealed class FakeGatewayClient : IZatcaGatewayClient
    {
        public ZatcaGatewayResponse ClearanceResponse { get; set; } = new(IsSuccess: true, IsRejected: false);
        public ZatcaGatewayResponse ReportingResponse { get; set; } = new(IsSuccess: true, IsRejected: false);
        public int ClearanceCallCount { get; private set; }
        public int ReportingCallCount { get; private set; }

        public Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        {
            ClearanceCallCount++;
            return Task.FromResult(ClearanceResponse);
        }

        public Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default)
        {
            ReportingCallCount++;
            return Task.FromResult(ReportingResponse);
        }
    }

    private sealed class FakeOutboxRepository : IInvoiceOutboxRepository
    {
        public List<OutboxMessage> Messages { get; set; } = new();
        public List<(Guid MessageId, OutboxStatus Status, string? Error)> StatusUpdates { get; } = new();

        public Task SaveInvoiceWithOutboxAsync(Invoice invoice, HashChainRecord newChainRecord, OutboxMessage outboxMessage, CancellationToken cancellationToken = default)
        {
            Messages.Add(outboxMessage);
            return Task.CompletedTask;
        }

        public Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(int batchSize = 10, CancellationToken cancellationToken = default)
        {
            var pending = Messages
                .Where(m => m.Status == OutboxStatus.Pending)
                .OrderBy(m => m.InvoiceCounterValue)
                .Take(batchSize)
                .ToList();

            return Task.FromResult<IReadOnlyList<OutboxMessage>>(pending);
        }

        public Task UpdateOutboxStatusAsync(Guid messageId, OutboxStatus status, string? errorMessage = null, CancellationToken cancellationToken = default)
        {
            StatusUpdates.Add((messageId, status, errorMessage));
            var msg = Messages.FirstOrDefault(m => m.Id == messageId);
            if (msg is not null)
            {
                var newRetry = (status == OutboxStatus.Pending || status == OutboxStatus.Failed) && errorMessage != null
                    ? msg.RetryCount + 1
                    : msg.RetryCount;

                var updated = msg with { Status = status, ErrorMessage = errorMessage, RetryCount = newRetry };
                Messages.Remove(msg);
                Messages.Add(updated);
            }
            return Task.CompletedTask;
        }
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ClearanceMessage_TransitionsToCleared()
    {
        // Arrange
        var msgId = Guid.NewGuid();
        var outboxRepo = new FakeOutboxRepository
        {
            Messages = { new OutboxMessage(msgId, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml/>", "h1", DateTimeOffset.UtcNow, invoiceCounterValue: 1) }
        };
        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: true, IsRejected: false, ClearanceStatus: "CLEARED")
        };
        var processor = new OutboxProcessor(outboxRepo, gateway);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(1, processed);
        Assert.Equal(1, gateway.ClearanceCallCount);
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == msgId && u.Status == OutboxStatus.Cleared);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_ReportingMessage_TransitionsToReported()
    {
        // Arrange
        var msgId = Guid.NewGuid();
        var outboxRepo = new FakeOutboxRepository
        {
            Messages = { new OutboxMessage(msgId, Guid.NewGuid(), OutboxMessageType.Reporting, "<xml/>", "h1", DateTimeOffset.UtcNow, invoiceCounterValue: 1) }
        };
        var gateway = new FakeGatewayClient
        {
            ReportingResponse = new ZatcaGatewayResponse(IsSuccess: true, IsRejected: false, ReportingStatus: "REPORTED")
        };
        var processor = new OutboxProcessor(outboxRepo, gateway);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(1, processed);
        Assert.Equal(1, gateway.ReportingCallCount);
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == msgId && u.Status == OutboxStatus.Reported);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_TransientError_IncrementsRetryAndKeepsPendingUntilMaxRetries()
    {
        // Arrange
        var msgId = Guid.NewGuid();
        var outboxRepo = new FakeOutboxRepository
        {
            Messages = { new OutboxMessage(msgId, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml/>", "h1", DateTimeOffset.UtcNow, status: OutboxStatus.Pending, retryCount: 2, invoiceCounterValue: 1) }
        };
        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: false, IsRejected: false, ErrorMessage: "HTTP 503 Service Unavailable")
        };
        var processor = new OutboxProcessor(outboxRepo, gateway, maxRetries: 5);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(0, processed);
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == msgId && u.Status == OutboxStatus.Pending && u.Error == "HTTP 503 Service Unavailable");
        var updatedMsg = outboxRepo.Messages.First(m => m.Id == msgId);
        Assert.Equal(3, updatedMsg.RetryCount);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_TransientErrorReachingMaxRetries_TransitionsToFailed()
    {
        // Arrange
        var msgId = Guid.NewGuid();
        var outboxRepo = new FakeOutboxRepository
        {
            Messages = { new OutboxMessage(msgId, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml/>", "h1", DateTimeOffset.UtcNow, status: OutboxStatus.Pending, retryCount: 4, invoiceCounterValue: 1) }
        };
        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: false, IsRejected: false, ErrorMessage: "Persistent Timeout")
        };
        var processor = new OutboxProcessor(outboxRepo, gateway, maxRetries: 5);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(0, processed);
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == msgId && u.Status == OutboxStatus.Failed);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PermanentRejection_TransitionsDirectlyToRejectedWithoutRetrying()
    {
        // Arrange
        var msgId = Guid.NewGuid();
        var outboxRepo = new FakeOutboxRepository
        {
            Messages = { new OutboxMessage(msgId, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml/>", "h1", DateTimeOffset.UtcNow, status: OutboxStatus.Pending, retryCount: 0, invoiceCounterValue: 1) }
        };
        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: false, IsRejected: true, ErrorMessage: "BR-KSA-01: Invalid VAT ID format")
        };
        var processor = new OutboxProcessor(outboxRepo, gateway, maxRetries: 5);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(0, processed);
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == msgId && u.Status == OutboxStatus.Rejected);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_SequentialChainIntegrity_HaltsBatchOnTransientFailure()
    {
        // Arrange: Message 1 (ICV 1) fails transiently, Message 2 (ICV 2) must NOT be processed to prevent sequence gaps
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var outboxRepo = new FakeOutboxRepository
        {
            Messages =
            {
                new OutboxMessage(id1, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml-1/>", "h1", DateTimeOffset.UtcNow, invoiceCounterValue: 1),
                new OutboxMessage(id2, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml-2/>", "h2", DateTimeOffset.UtcNow, invoiceCounterValue: 2)
            }
        };

        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: false, IsRejected: false, ErrorMessage: "Gateway Busy")
        };

        var processor = new OutboxProcessor(outboxRepo, gateway);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(0, processed);
        Assert.Equal(1, gateway.ClearanceCallCount); // ONLY the first message was attempted!
        Assert.DoesNotContain(outboxRepo.StatusUpdates, u => u.MessageId == id2);
    }

    [Fact]
    public async Task ProcessPendingMessagesAsync_PermanentRejection_HaltsSubsequentQueuedMessagesInBatch()
    {
        // Arrange: Message 1 (ICV 1) is rejected permanently by ZATCA, Message 2 (ICV 2) must NOT be processed
        var id1 = Guid.NewGuid();
        var id2 = Guid.NewGuid();

        var outboxRepo = new FakeOutboxRepository
        {
            Messages =
            {
                new OutboxMessage(id1, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml-1/>", "h1", DateTimeOffset.UtcNow, invoiceCounterValue: 1),
                new OutboxMessage(id2, Guid.NewGuid(), OutboxMessageType.Clearance, "<xml-2/>", "h2", DateTimeOffset.UtcNow, invoiceCounterValue: 2)
            }
        };

        var gateway = new FakeGatewayClient
        {
            ClearanceResponse = new ZatcaGatewayResponse(IsSuccess: false, IsRejected: true, ErrorMessage: "BR-KSA-01: Invalid VAT ID format")
        };

        var processor = new OutboxProcessor(outboxRepo, gateway);

        // Act
        var processed = await processor.ProcessPendingMessagesAsync(10);

        // Assert
        Assert.Equal(0, processed);
        Assert.Equal(1, gateway.ClearanceCallCount); // ONLY the first message was attempted
        Assert.Contains(outboxRepo.StatusUpdates, u => u.MessageId == id1 && u.Status == OutboxStatus.Rejected);
        Assert.DoesNotContain(outboxRepo.StatusUpdates, u => u.MessageId == id2);
    }
}

using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Application.Models;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Port for atomically persisting an issued invoice, its updated hash chain record, and its transactional outbox message.
/// </summary>
public interface IInvoiceOutboxRepository
{
    /// <summary>
    /// Atomically persists the invoice aggregate, the new hash chain record, and the outbox message in a single transaction.
    /// </summary>
    Task SaveInvoiceWithOutboxAsync(
        Invoice invoice,
        HashChainRecord newChainRecord,
        OutboxMessage outboxMessage,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a batch of pending outbox messages ordered strictly by sequence ICV.
    /// </summary>
    Task<IReadOnlyList<OutboxMessage>> GetPendingMessagesAsync(
        int batchSize = 10,
        CancellationToken cancellationToken = default);

    /// <summary>
    /// Updates the status, error details, and execution timestamps for a processed outbox message.
    /// </summary>
    Task UpdateOutboxStatusAsync(
        Guid messageId,
        OutboxStatus status,
        string? errorMessage = null,
        CancellationToken cancellationToken = default);
}

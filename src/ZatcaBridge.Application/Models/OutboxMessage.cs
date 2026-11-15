using System;

namespace ZatcaBridge.Application.Models;

/// <summary>
/// ZATCA dispatch operation type.
/// </summary>
public enum OutboxMessageType
{
    /// <summary>
    /// Synchronous real-time clearance for B2B standard tax invoices.
    /// </summary>
    Clearance,

    /// <summary>
    /// Asynchronous 24-hour reporting for B2C simplified tax invoices.
    /// </summary>
    Reporting
}

/// <summary>
/// Execution status of an outbox dispatch message.
/// </summary>
public enum OutboxStatus
{
    Pending = 0,
    Processing = 1,
    Cleared = 2,
    Reported = 3,
    Completed = 4,
    Failed = 5,
    Rejected = 6
}

/// <summary>
/// Transactional outbox entity for reliable dispatch to ZATCA APIs.
/// </summary>
public sealed record OutboxMessage
{
    public Guid Id { get; init; }
    public Guid InvoiceId { get; init; }
    public OutboxMessageType Type { get; init; }
    public string PayloadXml { get; init; }
    public string InvoiceHash { get; init; }
    public DateTimeOffset CreatedAt { get; init; }
    public OutboxStatus Status { get; init; }
    public int RetryCount { get; init; }
    public string? ErrorMessage { get; init; }
    public long InvoiceCounterValue { get; init; }

    public OutboxMessage(
        Guid id,
        Guid invoiceId,
        OutboxMessageType type,
        string payloadXml,
        string invoiceHash,
        DateTimeOffset createdAt,
        OutboxStatus status = OutboxStatus.Pending,
        int retryCount = 0,
        string? errorMessage = null,
        long invoiceCounterValue = 0)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Outbox message Id cannot be empty.", nameof(id));
        }

        if (invoiceId == Guid.Empty)
        {
            throw new ArgumentException("Invoice Id cannot be empty.", nameof(invoiceId));
        }

        if (string.IsNullOrWhiteSpace(payloadXml))
        {
            throw new ArgumentException("Payload XML cannot be null or whitespace.", nameof(payloadXml));
        }

        if (string.IsNullOrWhiteSpace(invoiceHash))
        {
            throw new ArgumentException("Invoice hash cannot be null or whitespace.", nameof(invoiceHash));
        }

        Id = id;
        InvoiceId = invoiceId;
        Type = type;
        PayloadXml = payloadXml;
        InvoiceHash = invoiceHash;
        CreatedAt = createdAt;
        Status = status;
        RetryCount = retryCount;
        ErrorMessage = errorMessage;
        InvoiceCounterValue = invoiceCounterValue;
    }
}

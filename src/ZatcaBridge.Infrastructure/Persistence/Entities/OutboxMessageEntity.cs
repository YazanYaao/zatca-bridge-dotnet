using System;

namespace ZatcaBridge.Infrastructure.Persistence.Entities;

public class OutboxMessageEntity
{
    public Guid Id { get; set; }
    public Guid InvoiceId { get; set; }
    public int Type { get; set; }
    public string PayloadXml { get; set; } = string.Empty;
    public string InvoiceHash { get; set; } = string.Empty;
    public long InvoiceCounterValue { get; set; }
    public int Status { get; set; }
    public int RetryCount { get; set; }
    public string? ErrorMessage { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
    public DateTimeOffset? ProcessedAt { get; set; }
}

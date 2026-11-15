using System;

namespace ZatcaBridge.Infrastructure.Persistence.Entities;

public class InvoiceEntity
{
    public Guid Id { get; set; }
    public Guid Uuid { get; set; }
    public string InvoiceNumber { get; set; } = string.Empty;
    public DateOnly IssueDate { get; set; }
    public TimeOnly IssueTime { get; set; }
    public int InvoiceTypeCode { get; set; }
    public int TransactionType { get; set; }
    public string SellerName { get; set; } = string.Empty;
    public string SellerVatNumber { get; set; } = string.Empty;
    public string? BuyerName { get; set; }
    public string? BuyerVatNumber { get; set; }
    public decimal LineExtensionAmount { get; set; }
    public decimal TaxExclusiveAmount { get; set; }
    public decimal TaxInclusiveAmount { get; set; }
    public decimal TotalTaxAmount { get; set; }
    public decimal PayableAmount { get; set; }
    public long InvoiceCounterValue { get; set; }
    public string? PreviousInvoiceHash { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

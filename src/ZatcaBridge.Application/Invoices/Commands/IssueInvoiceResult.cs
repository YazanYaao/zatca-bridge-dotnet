using System;

namespace ZatcaBridge.Application.Invoices.Commands;

/// <summary>
/// Result returned after successfully issuing an invoice and enqueuing its outbox dispatch.
/// </summary>
public sealed record IssueInvoiceResult(
    Guid InvoiceId,
    Guid Uuid,
    string InvoiceNumber,
    long InvoiceCounterValue,
    string PreviousInvoiceHash,
    string InvoiceHash,
    string QrCodeBase64,
    string UblXml,
    Guid OutboxMessageId
);

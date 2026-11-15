using System;
using System.Collections.Generic;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Application.Invoices.Commands;

/// <summary>
/// Data transfer object for a single line item within an invoice issuance command.
/// </summary>
public sealed record InvoiceLineItemDto(
    string Name,
    decimal Quantity,
    decimal UnitPrice,
    decimal DiscountAmount,
    TaxCategory TaxCategory
);

/// <summary>
/// Command for issuing a new compliant ZATCA Phase 2 invoice.
/// </summary>
public sealed record IssueInvoiceCommand(
    string InvoiceNumber,
    DateOnly IssueDate,
    TimeOnly IssueTime,
    InvoiceTypeCode InvoiceType,
    InvoiceTransactionType TransactionType,
    Party Seller,
    Party? Buyer,
    IEnumerable<InvoiceLineItemDto> LineItems,
    decimal AllowanceTotal = 0.00m,
    decimal ChargeTotal = 0.00m,
    decimal PrepaidAmount = 0.00m,
    decimal PayableRoundingAmount = 0.00m
);

using System;
using System.Collections.Generic;
using System.Linq;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Domain.Entities;

public class Invoice
{
    public Guid Id { get; }
    public Guid Uuid { get; }
    public string InvoiceNumber { get; }
    public DateOnly IssueDate { get; }
    public TimeOnly IssueTime { get; }
    public InvoiceTypeCode InvoiceType { get; }
    public InvoiceTransactionType TransactionType { get; }
    public Party Seller { get; }
    public Party? Buyer { get; }
    public IReadOnlyList<InvoiceLineItem> LineItems { get; }
    public IReadOnlyList<TaxSubtotal> TaxSubtotals { get; }
    public LegalMonetaryTotal MonetaryTotal { get; }
    public decimal TotalTaxAmount { get; }

    public long InvoiceCounterValue { get; }
    public string? PreviousInvoiceHash { get; }

    public Invoice(
        Guid id,
        Guid uuid,
        string invoiceNumber,
        DateOnly issueDate,
        TimeOnly issueTime,
        InvoiceTypeCode invoiceType,
        InvoiceTransactionType transactionType,
        Party seller,
        Party? buyer,
        IEnumerable<InvoiceLineItem> lineItems,
        decimal allowanceTotal = 0.00m,
        decimal chargeTotal = 0.00m,
        decimal prepaidAmount = 0.00m,
        decimal payableRoundingAmount = 0.00m,
        long invoiceCounterValue = 1,
        string? previousInvoiceHash = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Invoice Id cannot be empty.", nameof(id));
        }

        if (uuid == Guid.Empty)
        {
            throw new ArgumentException("Invoice UUID cannot be empty.", nameof(uuid));
        }

        if (string.IsNullOrWhiteSpace(invoiceNumber))
        {
            throw new ArgumentException("Invoice number cannot be empty or whitespace.", nameof(invoiceNumber));
        }

        ArgumentNullException.ThrowIfNull(seller);
        if (string.IsNullOrWhiteSpace(seller.VatNumber))
        {
            throw new ArgumentException("Seller must possess a valid KSA VAT Registration Number.", nameof(seller));
        }

        if (transactionType == InvoiceTransactionType.B2B)
        {
            if (buyer is null)
            {
                throw new ArgumentException("Buyer is mandatory for B2B transactions.", nameof(buyer));
            }

            if (string.IsNullOrWhiteSpace(buyer.VatNumber))
            {
                throw new ArgumentException("Buyer must possess a valid KSA VAT Registration Number for B2B transactions.", nameof(buyer));
            }
        }

        if (lineItems is null)
        {
            throw new ArgumentException("Line items cannot be null.", nameof(lineItems));
        }

        var itemsList = lineItems.ToList();
        if (itemsList.Count == 0)
        {
            throw new ArgumentException("An invoice must contain at least one line item.", nameof(lineItems));
        }

        if (allowanceTotal < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(allowanceTotal), "Allowance total cannot be negative.");
        }

        if (chargeTotal < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(chargeTotal), "Charge total cannot be negative.");
        }

        if (prepaidAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(prepaidAmount), "Prepaid amount cannot be negative.");
        }

        Id = id;
        Uuid = uuid;
        InvoiceNumber = invoiceNumber.Trim();
        IssueDate = issueDate;
        IssueTime = issueTime;
        InvoiceType = invoiceType;
        TransactionType = transactionType;
        Seller = seller;
        Buyer = buyer;
        LineItems = itemsList.AsReadOnly();
        InvoiceCounterValue = invoiceCounterValue;
        PreviousInvoiceHash = previousInvoiceHash;

        // Group tax subtotals by tax category
        var subtotals = itemsList
            .GroupBy(item => item.TaxCategory.Code)
            .Select(group =>
            {
                var category = group.First().TaxCategory;
                var taxableAmount = group.Sum(item => item.LineExtensionAmount);
                var taxAmount = group.Sum(item => item.TaxAmount);
                return new TaxSubtotal(category, taxableAmount, taxAmount);
            })
            .ToList();

        TaxSubtotals = subtotals.AsReadOnly();
        TotalTaxAmount = decimal.Round(subtotals.Sum(s => s.TaxAmount), 2, MidpointRounding.AwayFromZero);

        // Monetary totals calculation with MidpointRounding.AwayFromZero
        var lineExtensionSum = decimal.Round(itemsList.Sum(item => item.LineExtensionAmount), 2, MidpointRounding.AwayFromZero);
        var taxExclusiveAmount = decimal.Round(lineExtensionSum - allowanceTotal + chargeTotal, 2, MidpointRounding.AwayFromZero);
        var taxInclusiveAmount = decimal.Round(taxExclusiveAmount + TotalTaxAmount, 2, MidpointRounding.AwayFromZero);
        var payableAmount = decimal.Round(taxInclusiveAmount - prepaidAmount + payableRoundingAmount, 2, MidpointRounding.AwayFromZero);

        MonetaryTotal = new LegalMonetaryTotal(
            lineExtensionSum,
            taxExclusiveAmount,
            taxInclusiveAmount,
            allowanceTotal,
            chargeTotal,
            prepaidAmount,
            payableRoundingAmount,
            payableAmount
        );
    }
}

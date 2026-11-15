using System;

namespace ZatcaBridge.Domain.ValueObjects;

public sealed record TaxSubtotal
{
    public TaxCategory TaxCategory { get; }
    public decimal TaxableAmount { get; }
    public decimal TaxAmount { get; }

    public TaxSubtotal(TaxCategory taxCategory, decimal taxableAmount, decimal taxAmount)
    {
        ArgumentNullException.ThrowIfNull(taxCategory);

        if (taxableAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxableAmount), "Taxable amount cannot be negative.");
        }

        if (taxAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(taxAmount), "Tax amount cannot be negative.");
        }

        TaxCategory = taxCategory;
        // CRITICAL RULE 1: decimal math with MidpointRounding.AwayFromZero
        TaxableAmount = decimal.Round(taxableAmount, 2, MidpointRounding.AwayFromZero);
        TaxAmount = decimal.Round(taxAmount, 2, MidpointRounding.AwayFromZero);
    }
}

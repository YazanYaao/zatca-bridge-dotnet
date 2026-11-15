using System;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Domain.Entities;

public class InvoiceLineItem
{
    public Guid Id { get; }
    public string Name { get; }
    public decimal Quantity { get; }
    public decimal UnitPrice { get; }
    public decimal DiscountAmount { get; }
    public TaxCategory TaxCategory { get; }

    public decimal LineExtensionAmount { get; }
    public decimal TaxAmount { get; }
    public decimal TotalAmount { get; }

    public InvoiceLineItem(
        Guid id,
        string name,
        decimal quantity,
        decimal unitPrice,
        decimal discountAmount,
        TaxCategory taxCategory)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(name))
        {
            throw new ArgumentException("Line item name cannot be empty or whitespace.", nameof(name));
        }

        if (quantity <= 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(quantity), "Quantity must be greater than zero.");
        }

        if (unitPrice < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(unitPrice), "Unit price cannot be negative.");
        }

        if (discountAmount < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(discountAmount), "Discount amount cannot be negative.");
        }

        ArgumentNullException.ThrowIfNull(taxCategory);

        decimal grossAmount = quantity * unitPrice;
        if (discountAmount > grossAmount)
        {
            throw new ArgumentException("Discount amount cannot exceed the gross line amount.", nameof(discountAmount));
        }

        Id = id;
        Name = name;
        Quantity = quantity;
        UnitPrice = unitPrice;
        DiscountAmount = discountAmount;
        TaxCategory = taxCategory;

        // CRITICAL RULE 1: decimal math with MidpointRounding.AwayFromZero
        LineExtensionAmount = decimal.Round(grossAmount - discountAmount, 2, MidpointRounding.AwayFromZero);
        TaxAmount = decimal.Round(LineExtensionAmount * taxCategory.Rate, 2, MidpointRounding.AwayFromZero);
        TotalAmount = LineExtensionAmount + TaxAmount;
    }
}

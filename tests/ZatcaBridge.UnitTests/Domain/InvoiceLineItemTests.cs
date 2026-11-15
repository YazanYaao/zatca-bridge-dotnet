using System;
using Xunit;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Domain;

public class InvoiceLineItemTests
{
    [Fact]
    public void Constructor_ValidParameters_CalculatesLineExtensionTaxAndTotalAmounts()
    {
        // Arrange
        var id = Guid.NewGuid();
        var name = "Standard Consulting Service";
        var quantity = 2.00m;
        var unitPrice = 50.00m;
        var discount = 0.00m;
        var taxCategory = TaxCategory.Standard;

        // Act
        var lineItem = new InvoiceLineItem(id, name, quantity, unitPrice, discount, taxCategory);

        // Assert
        Assert.Equal(id, lineItem.Id);
        Assert.Equal(name, lineItem.Name);
        Assert.Equal(quantity, lineItem.Quantity);
        Assert.Equal(unitPrice, lineItem.UnitPrice);
        Assert.Equal(discount, lineItem.DiscountAmount);
        Assert.Equal(100.00m, lineItem.LineExtensionAmount);
        Assert.Equal(15.00m, lineItem.TaxAmount);
        Assert.Equal(115.00m, lineItem.TotalAmount);
        Assert.Equal(TaxCategoryCode.Standard, lineItem.TaxCategory.Code);
    }

    [Fact]
    public void Constructor_WithDiscount_DeductsDiscountBeforeTaxCalculation()
    {
        // Arrange
        var quantity = 3.00m;
        var unitPrice = 100.00m;
        var discount = 20.00m; // Gross = 300.00, Net = 280.00
        var taxCategory = TaxCategory.Standard;

        // Act
        var lineItem = new InvoiceLineItem(Guid.NewGuid(), "Software License", quantity, unitPrice, discount, taxCategory);

        // Assert
        Assert.Equal(280.00m, lineItem.LineExtensionAmount);
        Assert.Equal(42.00m, lineItem.TaxAmount); // 280 * 0.15 = 42.00
        Assert.Equal(322.00m, lineItem.TotalAmount);
    }

    [Fact]
    public void Constructor_ZeroRatedTaxCategory_CalculatesZeroTax()
    {
        // Arrange
        var taxCategory = TaxCategory.ZeroRated;

        // Act
        var lineItem = new InvoiceLineItem(Guid.NewGuid(), "Export Goods", 5.00m, 20.00m, 0.00m, taxCategory);

        // Assert
        Assert.Equal(100.00m, lineItem.LineExtensionAmount);
        Assert.Equal(0.00m, lineItem.TaxAmount);
        Assert.Equal(100.00m, lineItem.TotalAmount);
        Assert.Equal(TaxCategoryCode.ZeroRated, lineItem.TaxCategory.Code);
    }

    [Fact]
    public void Constructor_ExemptTaxCategory_CalculatesZeroTax()
    {
        // Arrange
        var taxCategory = TaxCategory.Exempt("VATEX-SA-29", "Financial services");

        // Act
        var lineItem = new InvoiceLineItem(Guid.NewGuid(), "Banking Fee", 1.00m, 250.00m, 0.00m, taxCategory);

        // Assert
        Assert.Equal(250.00m, lineItem.LineExtensionAmount);
        Assert.Equal(0.00m, lineItem.TaxAmount);
        Assert.Equal(250.00m, lineItem.TotalAmount);
        Assert.Equal(TaxCategoryCode.Exempt, lineItem.TaxCategory.Code);
        Assert.Equal("VATEX-SA-29", lineItem.TaxCategory.ExemptionReasonCode);
    }

    [Fact]
    public void Constructor_OutOfScopeTaxCategory_CalculatesZeroTax()
    {
        // Arrange
        var taxCategory = TaxCategory.OutOfScope;

        // Act
        var lineItem = new InvoiceLineItem(Guid.NewGuid(), "Government Levy", 1.00m, 100.00m, 0.00m, taxCategory);

        // Assert
        Assert.Equal(100.00m, lineItem.LineExtensionAmount);
        Assert.Equal(0.00m, lineItem.TaxAmount);
        Assert.Equal(100.00m, lineItem.TotalAmount);
        Assert.Equal(TaxCategoryCode.OutOfScope, lineItem.TaxCategory.Code);
    }

    [Fact]
    public void Constructor_AwayFromZeroRounding_RoundsUpOnMidpoint()
    {
        // Half-cent rounding test: 10.085m must round to 10.09m under AwayFromZero (ToEven would round to 10.08m)
        var lineItem = new InvoiceLineItem(
            Guid.NewGuid(),
            "Precision Item",
            quantity: 1.00m,
            unitPrice: 10.085m,
            discountAmount: 0.00m,
            taxCategory: TaxCategory.Standard
        );

        Assert.Equal(10.09m, lineItem.LineExtensionAmount);
        // 10.09 * 0.15 = 1.5135 => rounds to 1.51
        Assert.Equal(1.51m, lineItem.TaxAmount);
        Assert.Equal(11.60m, lineItem.TotalAmount);
    }

    [Fact]
    public void Constructor_TaxAmountAwayFromZeroRounding_RoundsMidpointCorrectly()
    {
        // LineExtension: 1.05m => 1.05 * 0.15 = 0.1575 => rounds to 0.16m
        var lineItem = new InvoiceLineItem(
            Guid.NewGuid(),
            "Tax Precision Item",
            quantity: 1.00m,
            unitPrice: 1.05m,
            discountAmount: 0.00m,
            taxCategory: TaxCategory.Standard
        );

        Assert.Equal(1.05m, lineItem.LineExtensionAmount);
        Assert.Equal(0.16m, lineItem.TaxAmount);
        Assert.Equal(1.21m, lineItem.TotalAmount);
    }

    [Theory]
    [InlineData(-1.0)]
    [InlineData(0.0)]
    public void Constructor_NonPositiveQuantity_ThrowsArgumentOutOfRangeException(decimal invalidQuantity)
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), "Item", invalidQuantity, 10.00m, 0.00m, TaxCategory.Standard));
    }

    [Fact]
    public void Constructor_NegativeUnitPrice_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, -0.01m, 0.00m, TaxCategory.Standard));
    }

    [Fact]
    public void Constructor_NegativeDiscount_ThrowsArgumentOutOfRangeException()
    {
        Assert.Throws<ArgumentOutOfRangeException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, -0.01m, TaxCategory.Standard));
    }

    [Fact]
    public void Constructor_DiscountExceedsGrossAmount_ThrowsArgumentException()
    {
        Assert.Throws<ArgumentException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), "Item", 2.00m, 10.00m, 25.00m, TaxCategory.Standard));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidName_ThrowsArgumentException(string? invalidName)
    {
        Assert.Throws<ArgumentException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), invalidName!, 1.00m, 10.00m, 0.00m, TaxCategory.Standard));
    }

    [Fact]
    public void Constructor_NullTaxCategory_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, null!));
    }
}

using System;
using System.Collections.Generic;
using System.Linq;
using Xunit;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.UnitTests.Domain;

public class InvoiceAggregateTests
{
    private static Party CreateValidSeller() =>
        new Party("Acme Saudi Trading Co", "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA");

    private static Party CreateValidBuyer() =>
        new Party("Retail Partner LLC", "320987654300003", "King Fahd Rd", "5678", "12334", "Riyadh", "Nakheel", "SA");

    [Fact]
    public void Constructor_ValidB2BInvoice_CalculatesMonetaryTotalsAndTaxSubtotalsCorrectly()
    {
        // Arrange
        var seller = CreateValidSeller();
        var buyer = CreateValidBuyer();

        var line1 = new InvoiceLineItem(Guid.NewGuid(), "Cloud Hosting", 2.00m, 100.00m, 0.00m, TaxCategory.Standard); // Net: 200, Tax: 30
        var line2 = new InvoiceLineItem(Guid.NewGuid(), "Support Services", 1.00m, 50.00m, 10.00m, TaxCategory.Standard); // Net: 40, Tax: 6
        var line3 = new InvoiceLineItem(Guid.NewGuid(), "International Consulting", 1.00m, 300.00m, 0.00m, TaxCategory.ZeroRated); // Net: 300, Tax: 0

        var lineItems = new List<InvoiceLineItem> { line1, line2, line3 };
        var id = Guid.NewGuid();
        var uuid = Guid.NewGuid();
        var issueDate = new DateOnly(2026, 11, 16);
        var issueTime = new TimeOnly(10, 30, 0);

        // Act
        var invoice = new Invoice(
            id,
            uuid,
            "INV-2026-0001",
            issueDate,
            issueTime,
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            seller,
            buyer,
            lineItems
        );

        // Assert
        Assert.Equal(id, invoice.Id);
        Assert.Equal(uuid, invoice.Uuid);
        Assert.Equal("INV-2026-0001", invoice.InvoiceNumber);
        Assert.Equal(issueDate, invoice.IssueDate);
        Assert.Equal(issueTime, invoice.IssueTime);
        Assert.Equal(InvoiceTypeCode.TaxInvoice, invoice.InvoiceType);
        Assert.Equal(InvoiceTransactionType.B2B, invoice.TransactionType);
        Assert.Equal(seller, invoice.Seller);
        Assert.Equal(buyer, invoice.Buyer);
        Assert.Equal(3, invoice.LineItems.Count);

        // Monetary Totals verification
        // LineExtension: 200 + 40 + 300 = 540.00
        Assert.Equal(540.00m, invoice.MonetaryTotal.LineExtensionAmount);
        Assert.Equal(540.00m, invoice.MonetaryTotal.TaxExclusiveAmount);
        // TaxTotal: 30 + 6 + 0 = 36.00
        Assert.Equal(36.00m, invoice.TotalTaxAmount);
        // TaxInclusive: 540 + 36 = 576.00
        Assert.Equal(576.00m, invoice.MonetaryTotal.TaxInclusiveAmount);
        Assert.Equal(576.00m, invoice.MonetaryTotal.PayableAmount);

        // Tax Subtotals verification (grouped by tax category code)
        Assert.Equal(2, invoice.TaxSubtotals.Count);

        var standardSubtotal = invoice.TaxSubtotals.FirstOrDefault(s => s.TaxCategory.Code == TaxCategoryCode.Standard);
        Assert.NotNull(standardSubtotal);
        Assert.Equal(240.00m, standardSubtotal.TaxableAmount); // 200 + 40
        Assert.Equal(36.00m, standardSubtotal.TaxAmount); // 30 + 6

        var zeroRatedSubtotal = invoice.TaxSubtotals.FirstOrDefault(s => s.TaxCategory.Code == TaxCategoryCode.ZeroRated);
        Assert.NotNull(zeroRatedSubtotal);
        Assert.Equal(300.00m, zeroRatedSubtotal.TaxableAmount);
        Assert.Equal(0.00m, zeroRatedSubtotal.TaxAmount);
    }

    [Fact]
    public void Constructor_WithAllowanceChargePrepaidAndRounding_CalculatesTotalsCorrectly()
    {
        // Arrange
        var seller = CreateValidSeller();
        var buyer = CreateValidBuyer();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Hardware Server", 1.00m, 1000.00m, 0.00m, TaxCategory.Standard); // Net: 1000, Tax: 150

        // Act
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-2026-0002",
            new DateOnly(2026, 11, 16),
            new TimeOnly(12, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            seller,
            buyer,
            new[] { line },
            allowanceTotal: 50.00m,
            chargeTotal: 20.00m,
            prepaidAmount: 200.00m,
            payableRoundingAmount: 0.05m
        );

        // Assert
        // LineExtension: 1000.00
        Assert.Equal(1000.00m, invoice.MonetaryTotal.LineExtensionAmount);
        // TaxExclusive = 1000.00 - 50.00 + 20.00 = 970.00
        Assert.Equal(970.00m, invoice.MonetaryTotal.TaxExclusiveAmount);
        // TaxTotal = 150.00
        Assert.Equal(150.00m, invoice.TotalTaxAmount);
        // TaxInclusive = 970.00 + 150.00 = 1120.00
        Assert.Equal(1120.00m, invoice.MonetaryTotal.TaxInclusiveAmount);
        // PayableAmount = 1120.00 - 200.00 + 0.05 = 920.05
        Assert.Equal(920.05m, invoice.MonetaryTotal.PayableAmount);
        Assert.Equal(50.00m, invoice.MonetaryTotal.AllowanceTotalAmount);
        Assert.Equal(20.00m, invoice.MonetaryTotal.ChargeTotalAmount);
        Assert.Equal(200.00m, invoice.MonetaryTotal.PrepaidAmount);
        Assert.Equal(0.05m, invoice.MonetaryTotal.PayableRoundingAmount);
    }

    [Fact]
    public void Constructor_ValidB2CInvoiceWithoutBuyer_InstantiatesSuccessfully()
    {
        // Arrange
        var seller = CreateValidSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Coffee", 1.00m, 15.00m, 0.00m, TaxCategory.Standard);

        // Act
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-B2C-0001",
            new DateOnly(2026, 11, 16),
            new TimeOnly(14, 0, 0),
            InvoiceTypeCode.SimplifiedTaxInvoice,
            InvoiceTransactionType.B2C,
            seller,
            buyer: null,
            new[] { line }
        );

        // Assert
        Assert.Null(invoice.Buyer);
        Assert.Equal(InvoiceTransactionType.B2C, invoice.TransactionType);
        Assert.Equal(InvoiceTypeCode.SimplifiedTaxInvoice, invoice.InvoiceType);
        Assert.Equal(15.00m, invoice.MonetaryTotal.LineExtensionAmount);
        Assert.Equal(2.25m, invoice.TotalTaxAmount);
        Assert.Equal(17.25m, invoice.MonetaryTotal.PayableAmount);
    }

    [Fact]
    public void Constructor_B2BInvoiceWithoutBuyer_ThrowsArgumentException()
    {
        var seller = CreateValidSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard);

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.TaxInvoice,
                InvoiceTransactionType.B2B,
                seller,
                buyer: null,
                new[] { line }
            ));
    }

    [Fact]
    public void Constructor_B2BInvoiceBuyerWithoutVatNumber_ThrowsArgumentException()
    {
        var seller = CreateValidSeller();
        var buyerWithoutVat = new Party("Buyer Without VAT", null, "Street", "1", "12345", "Riyadh", "District", "SA");
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard);

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.TaxInvoice,
                InvoiceTransactionType.B2B,
                seller,
                buyer: buyerWithoutVat,
                new[] { line }
            ));
    }

    [Fact]
    public void Constructor_SellerWithoutVatNumber_ThrowsArgumentException()
    {
        var sellerWithoutVat = new Party("Seller No VAT", null, "Street", "1", "12345", "Riyadh", "District", "SA");
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard);

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.SimplifiedTaxInvoice,
                InvoiceTransactionType.B2C,
                sellerWithoutVat,
                buyer: null,
                new[] { line }
            ));
    }

    [Fact]
    public void Constructor_EmptyLineItems_ThrowsArgumentException()
    {
        var seller = CreateValidSeller();

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.NewGuid(),
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.SimplifiedTaxInvoice,
                InvoiceTransactionType.B2C,
                seller,
                buyer: null,
                Array.Empty<InvoiceLineItem>()
            ));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Constructor_InvalidInvoiceNumber_ThrowsArgumentException(string? invalidNumber)
    {
        var seller = CreateValidSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard);

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.NewGuid(),
                invalidNumber!,
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.SimplifiedTaxInvoice,
                InvoiceTransactionType.B2C,
                seller,
                buyer: null,
                new[] { line }
            ));
    }

    [Fact]
    public void Constructor_EmptyIdOrUuid_ThrowsArgumentException()
    {
        var seller = CreateValidSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 10.00m, 0.00m, TaxCategory.Standard);

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.Empty,
                Guid.NewGuid(),
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.SimplifiedTaxInvoice,
                InvoiceTransactionType.B2C,
                seller,
                buyer: null,
                new[] { line }
            ));

        Assert.Throws<ArgumentException>(() =>
            new Invoice(
                Guid.NewGuid(),
                Guid.Empty,
                "INV-001",
                new DateOnly(2026, 11, 16),
                new TimeOnly(10, 0, 0),
                InvoiceTypeCode.SimplifiedTaxInvoice,
                InvoiceTransactionType.B2C,
                seller,
                buyer: null,
                new[] { line }
            ));
    }

    [Theory]
    [InlineData("310123456700003")] // Valid
    [InlineData("399999999999993")] // Valid
    public void Party_ValidKsaVatNumber_InstantiatesSuccessfully(string vatNumber)
    {
        var party = new Party("Valid Co", vatNumber, "Street", "1", "12345", "Riyadh", "District", "SA");
        Assert.Equal(vatNumber, party.VatNumber);
    }

    [Theory]
    [InlineData("110123456700003")] // Does not start with 3
    [InlineData("310123456700001")] // Does not end with 3
    [InlineData("3101234567")]      // Too short (10 digits)
    [InlineData("31012345670000003")] // Too long (17 digits)
    [InlineData("3101234567000A3")] // Contains letters
    public void Party_InvalidKsaVatNumber_ThrowsArgumentException(string invalidVatNumber)
    {
        Assert.Throws<ArgumentException>(() =>
            new Party("Invalid Co", invalidVatNumber, "Street", "1", "12345", "Riyadh", "District", "SA"));
    }

    [Theory]
    [InlineData(null)]
    [InlineData("")]
    [InlineData("   ")]
    public void Party_InvalidRegistrationName_ThrowsArgumentException(string? invalidName)
    {
        Assert.Throws<ArgumentException>(() =>
            new Party(invalidName!, "310123456700003", "Street", "1", "12345", "Riyadh", "District", "SA"));
    }
}

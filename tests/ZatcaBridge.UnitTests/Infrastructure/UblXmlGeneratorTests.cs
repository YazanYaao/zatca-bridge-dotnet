using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Xml.Linq;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class UblXmlGeneratorTests
{
    private static readonly XNamespace NsInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace NsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace NsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";

    private static Party CreateSeller(string name = "Acme Saudi Trading Co") =>
        new Party(name, "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA");

    private static Party CreateBuyer(string name = "Retail Partner LLC") =>
        new Party(name, "320987654300003", "King Fahd Rd", "5678", "12334", "Riyadh", "Nakheel", "SA");

    [Fact]
    public void GenerateXml_StandardB2BInvoice_ProducesValidUbl21XmlWithCorrectNamespaces()
    {
        // Arrange
        var seller = CreateSeller();
        var buyer = CreateBuyer();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Consulting Service", 1.00m, 1000.00m, 0.00m, TaxCategory.Standard);

        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-2026-0001",
            new DateOnly(2026, 11, 16),
            new TimeOnly(10, 30, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            seller,
            buyer,
            new[] { line },
            invoiceCounterValue: 10,
            previousInvoiceHash: "aGFzaC05"
        );

        var generator = new UblXmlGenerator();

        // Act
        var xmlString = generator.GenerateXml(invoice, qrCodeBase64: "cXVpY2stcmVzcG9uc2UtY29kZQ==");

        // Assert
        Assert.NotNull(xmlString);
        var doc = XDocument.Parse(xmlString);
        Assert.NotNull(doc.Root);

        Assert.Equal("Invoice", doc.Root.Name.LocalName);
        Assert.Equal(NsInvoice.NamespaceName, doc.Root.Name.NamespaceName);

        // Check ProfileID is clearance:1.0 for B2B
        var profileId = doc.Root.Element(NsCbc + "ProfileID")?.Value;
        Assert.Equal("clearance:1.0", profileId);

        // Check Invoice ID & UUID
        Assert.Equal("INV-2026-0001", doc.Root.Element(NsCbc + "ID")?.Value);
        Assert.Equal(invoice.Uuid.ToString(), doc.Root.Element(NsCbc + "UUID")?.Value);
        Assert.Equal("2026-11-16", doc.Root.Element(NsCbc + "IssueDate")?.Value);
        Assert.Equal("10:30:00", doc.Root.Element(NsCbc + "IssueTime")?.Value);
        Assert.Equal("388", doc.Root.Element(NsCbc + "InvoiceTypeCode")?.Value);
        Assert.Equal("SAR", doc.Root.Element(NsCbc + "DocumentCurrencyCode")?.Value);
        Assert.Equal("SAR", doc.Root.Element(NsCbc + "TaxCurrencyCode")?.Value);
    }

    [Fact]
    public void GenerateXml_SimplifiedB2CInvoice_SetsProfileIdToReporting()
    {
        // Arrange
        var seller = CreateSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Coffee", 2.00m, 15.00m, 0.00m, TaxCategory.Standard);

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

        var generator = new UblXmlGenerator();

        // Act
        var xmlString = generator.GenerateXml(invoice);

        // Assert
        var doc = XDocument.Parse(xmlString);
        Assert.NotNull(doc.Root);

        var profileId = doc.Root.Element(NsCbc + "ProfileID")?.Value;
        Assert.Equal("reporting:1.0", profileId);

        // B2C without buyer should not have AccountingCustomerParty or should have an empty/anonymous party
        var customerParty = doc.Root.Element(NsCac + "AccountingCustomerParty");
        Assert.Null(customerParty);
    }

    [Fact]
    public void GenerateXml_EmbeddedDocumentReferences_IncludesIcvPihAndQr()
    {
        // Arrange
        var seller = CreateSeller();
        var line = new InvoiceLineItem(Guid.NewGuid(), "Item", 1.00m, 50.00m, 0.00m, TaxCategory.Standard);
        const long icv = 42;
        const string pih = "cHJldmlvdXMtaGFzaA==";
        const string qr = "cXVpY2stcmVzcG9uc2UtY29kZQ==";

        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-042",
            new DateOnly(2026, 11, 16),
            new TimeOnly(15, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            seller,
            CreateBuyer(),
            new[] { line },
            invoiceCounterValue: icv,
            previousInvoiceHash: pih
        );

        var generator = new UblXmlGenerator();

        // Act
        var xmlString = generator.GenerateXml(invoice, qrCodeBase64: qr);

        // Assert
        var doc = XDocument.Parse(xmlString);
        var docRefs = doc.Root!.Elements(NsCac + "AdditionalDocumentReference").ToList();

        // 1. ICV reference
        var icvRef = docRefs.FirstOrDefault(r => r.Element(NsCbc + "ID")?.Value == "ICV");
        Assert.NotNull(icvRef);
        Assert.Equal("42", icvRef.Element(NsCbc + "UUID")?.Value);

        // 2. PIH reference
        var pihRef = docRefs.FirstOrDefault(r => r.Element(NsCbc + "ID")?.Value == "PIH");
        Assert.NotNull(pihRef);
        var pihValue = pihRef.Element(NsCac + "Attachment")
            ?.Element(NsCbc + "EmbeddedDocumentBinaryObject")?.Value;
        Assert.Equal(pih, pihValue);

        // 3. QR reference
        var qrRef = docRefs.FirstOrDefault(r => r.Element(NsCbc + "ID")?.Value == "QR");
        Assert.NotNull(qrRef);
        var qrValue = qrRef.Element(NsCac + "Attachment")
            ?.Element(NsCbc + "EmbeddedDocumentBinaryObject")?.Value;
        Assert.Equal(qr, qrValue);
    }

    [Fact]
    public void GenerateXml_TaxAndMonetaryTotals_IncludesSarCurrencyIdAndExactDecimalFormatting()
    {
        // Arrange
        var seller = CreateSeller();
        var buyer = CreateBuyer();
        var line1 = new InvoiceLineItem(Guid.NewGuid(), "Item 1", 2.00m, 100.00m, 0.00m, TaxCategory.Standard); // Net: 200.00, Tax: 30.00
        var line2 = new InvoiceLineItem(Guid.NewGuid(), "Item 2", 1.00m, 50.00m, 0.00m, TaxCategory.ZeroRated); // Net: 50.00, Tax: 0.00

        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-MONEY-01",
            new DateOnly(2026, 11, 16),
            new TimeOnly(10, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            seller,
            buyer,
            new[] { line1, line2 },
            allowanceTotal: 10.00m,
            chargeTotal: 5.00m,
            prepaidAmount: 50.00m,
            payableRoundingAmount: 0.05m
        );

        var generator = new UblXmlGenerator();

        // Act
        var xmlString = generator.GenerateXml(invoice);
        var doc = XDocument.Parse(xmlString);

        // Assert
        var legalTotal = doc.Root!.Element(NsCac + "LegalMonetaryTotal");
        Assert.NotNull(legalTotal);

        Assert.Equal("250.00", legalTotal.Element(NsCbc + "LineExtensionAmount")?.Value);
        Assert.Equal("SAR", legalTotal.Element(NsCbc + "LineExtensionAmount")?.Attribute("currencyID")?.Value);

        Assert.Equal("245.00", legalTotal.Element(NsCbc + "TaxExclusiveAmount")?.Value);
        Assert.Equal("SAR", legalTotal.Element(NsCbc + "TaxExclusiveAmount")?.Attribute("currencyID")?.Value);

        Assert.Equal("275.00", legalTotal.Element(NsCbc + "TaxInclusiveAmount")?.Value);
        Assert.Equal("SAR", legalTotal.Element(NsCbc + "TaxInclusiveAmount")?.Attribute("currencyID")?.Value);

        Assert.Equal("10.00", legalTotal.Element(NsCbc + "AllowanceTotalAmount")?.Value);
        Assert.Equal("5.00", legalTotal.Element(NsCbc + "ChargeTotalAmount")?.Value);
        Assert.Equal("50.00", legalTotal.Element(NsCbc + "PrepaidAmount")?.Value);
        Assert.Equal("0.05", legalTotal.Element(NsCbc + "PayableRoundingAmount")?.Value);

        Assert.Equal("225.05", legalTotal.Element(NsCbc + "PayableAmount")?.Value);
        Assert.Equal("SAR", legalTotal.Element(NsCbc + "PayableAmount")?.Attribute("currencyID")?.Value);

        // TaxTotal assertions
        var taxTotal = doc.Root.Element(NsCac + "TaxTotal");
        Assert.NotNull(taxTotal);
        Assert.Equal("30.00", taxTotal.Element(NsCbc + "TaxAmount")?.Value);
        Assert.Equal("SAR", taxTotal.Element(NsCbc + "TaxAmount")?.Attribute("currencyID")?.Value);

        var subtotals = taxTotal.Elements(NsCac + "TaxSubtotal").ToList();
        Assert.Equal(2, subtotals.Count);
    }

    [Fact]
    public void GenerateXml_ArabicContent_PreservesUtf8WithoutBom()
    {
        // Arrange
        const string arabicSellerName = "شركة أكمي للتجارة المحدودة";
        const string arabicItemName = "خدمات استشارات تقنية";

        var seller = CreateSeller(arabicSellerName);
        var line = new InvoiceLineItem(Guid.NewGuid(), arabicItemName, 1.00m, 500.00m, 0.00m, TaxCategory.Standard);

        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-AR-001",
            new DateOnly(2026, 11, 16),
            new TimeOnly(11, 0, 0),
            InvoiceTypeCode.SimplifiedTaxInvoice,
            InvoiceTransactionType.B2C,
            seller,
            buyer: null,
            new[] { line }
        );

        var generator = new UblXmlGenerator();

        // Act
        var xmlBytes = generator.GenerateXmlBytes(invoice);
        var xmlString = Encoding.UTF8.GetString(xmlBytes);

        // Assert
        // Verify NO UTF-8 Byte Order Mark (BOM = 0xEF, 0xBB, 0xBF)
        Assert.False(xmlBytes.Length >= 3 && xmlBytes[0] == 0xEF && xmlBytes[1] == 0xBB && xmlBytes[2] == 0xBF,
            "Generated XML bytes must not include a UTF-8 BOM.");

        // Verify Arabic text preservation in parsed XML
        var doc = XDocument.Parse(xmlString);
        var registrationName = doc.Root!
            .Element(NsCac + "AccountingSupplierParty")
            ?.Element(NsCac + "Party")
            ?.Element(NsCac + "PartyLegalEntity")
            ?.Element(NsCbc + "RegistrationName")?.Value;

        Assert.Equal(arabicSellerName, registrationName);

        var itemName = doc.Root
            .Element(NsCac + "InvoiceLine")
            ?.Element(NsCac + "Item")
            ?.Element(NsCbc + "Name")?.Value;

        Assert.Equal(arabicItemName, itemName);
    }

    [Fact]
    public void GenerateXml_NullInvoice_ThrowsArgumentNullException()
    {
        var generator = new UblXmlGenerator();
        Assert.Throws<ArgumentNullException>(() => generator.GenerateXml(null!));
        Assert.Throws<ArgumentNullException>(() => generator.GenerateXmlBytes(null!));
    }
}

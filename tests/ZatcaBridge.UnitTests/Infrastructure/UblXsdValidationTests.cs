using System;
using Xunit;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Xml;
using ZatcaBridge.UnitTests.Helpers;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class UblXsdValidationTests
{
    private readonly UblXmlGenerator _generator = new();

    private static Party CreateSeller() =>
        new Party("Acme Saudi Co", "310123456700003", "Olaya St", "1234", "12211", "Riyadh", "Olaya", "SA", crn: "1010010000");

    private static Party CreateBuyer() =>
        new Party("Buyer Corp", "310987654300003", "Tahlia St", "5678", "12334", "Riyadh", "Sulaimaniyah", "SA", crn: "1010020000");

    [Fact]
    public void B2B_StandardInvoice_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-B2B-XSD-001",
            new DateOnly(2026, 11, 18),
            new TimeOnly(12, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Standard Consulting", 1, 1000m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void B2C_SimplifiedInvoice_WithoutBuyer_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-B2C-XSD-002",
            new DateOnly(2026, 11, 18),
            new TimeOnly(14, 30, 0),
            InvoiceTypeCode.SimplifiedTaxInvoice,
            InvoiceTransactionType.B2C,
            CreateSeller(),
            buyer: null,
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Retail Coffee & Pastry", 2, 25m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 2,
            previousInvoiceHash: "aGFzaC0wMQ=="
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void ZeroRated_Invoice_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-ZERO-XSD-003",
            new DateOnly(2026, 11, 18),
            new TimeOnly(10, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Export Item Zero-Rated", 5, 200m, 0m, TaxCategory.ZeroRated) },
            invoiceCounterValue: 3,
            previousInvoiceHash: "aGFzaC0wMg=="
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void Exempt_Invoice_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-EXEMPT-XSD-004",
            new DateOnly(2026, 11, 18),
            new TimeOnly(11, 15, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Financial Qualifying Service", 1, 5000m, 0m, TaxCategory.Exempt("VATEX-SA-29", "Financial services")) },
            invoiceCounterValue: 4,
            previousInvoiceHash: "aGFzaC0wMw=="
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void CreditNote_ValidatesAgainstUbl21Xsd()
    {
        // Arrange (Credit Note 381)
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "CN-XSD-005",
            new DateOnly(2026, 11, 18),
            new TimeOnly(15, 0, 0),
            InvoiceTypeCode.CreditNote,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Returned Hardware Goods", 1, 300m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 5,
            previousInvoiceHash: "aGFzaC0wNA=="
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void DebitNote_ValidatesAgainstUbl21Xsd()
    {
        // Arrange (Debit Note 383)
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "DN-XSD-006",
            new DateOnly(2026, 11, 18),
            new TimeOnly(16, 20, 0),
            InvoiceTypeCode.DebitNote,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Additional Logistics Fee", 1, 150m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 6,
            previousInvoiceHash: "aGFzaC0wNQ=="
        );

        // Act
        var xml = _generator.GenerateXml(invoice, qrCodeBase64: "dGVzdC1xci1jb2Rl");

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(xml);
        Assert.True(isValid, $"XSD Validation failed with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void Signed_B2C_SimplifiedInvoice_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-B2C-SIGNED-XSD",
            new DateOnly(2026, 11, 18),
            new TimeOnly(14, 30, 0),
            InvoiceTypeCode.SimplifiedTaxInvoice,
            InvoiceTransactionType.B2C,
            CreateSeller(),
            buyer: null,
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Retail Coffee", 2, 25m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var credential = CreateTestCredential();
        var signer = new ZatcaBridge.Infrastructure.Cryptography.XadesXmlSigner(_generator);

        // Act
        var result = signer.SignInvoice(invoice, credential);

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(result.SignedXml);
        Assert.True(isValid, $"XSD Validation failed for signed XML with errors: {string.Join("; ", errors)}");
    }

    [Fact]
    public void Signed_B2B_StandardInvoice_ValidatesAgainstUbl21Xsd()
    {
        // Arrange
        var invoice = new Invoice(
            Guid.NewGuid(),
            Guid.NewGuid(),
            "INV-B2B-SIGNED-XSD",
            new DateOnly(2026, 11, 18),
            new TimeOnly(12, 0, 0),
            InvoiceTypeCode.TaxInvoice,
            InvoiceTransactionType.B2B,
            CreateSeller(),
            CreateBuyer(),
            new[] { new InvoiceLineItem(Guid.NewGuid(), "Consulting", 1, 1000m, 0m, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var credential = CreateTestCredential();
        var signer = new ZatcaBridge.Infrastructure.Cryptography.XadesXmlSigner(_generator);

        // Act
        var result = signer.SignInvoice(invoice, credential);

        // Assert
        var (isValid, errors) = ZatcaXsdValidator.Validate(result.SignedXml);
        Assert.True(isValid, $"XSD Validation failed for signed XML with errors: {string.Join("; ", errors)}");
    }

    private static CsidCredential CreateTestCredential()
    {
        var keyGen = new Org.BouncyCastle.Crypto.Generators.ECKeyPairGenerator();
        keyGen.Init(new Org.BouncyCastle.Crypto.Parameters.ECKeyGenerationParameters(
            Org.BouncyCastle.Asn1.Sec.SecObjectIdentifiers.SecP256k1, new Org.BouncyCastle.Security.SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var dn = new Org.BouncyCastle.Asn1.X509.X509Name("CN=TST-310123456700003, OU=Riyadh, O=Test, C=SA");
        var certGen = new Org.BouncyCastle.X509.X509V3CertificateGenerator();
        certGen.SetIssuerDN(dn);
        certGen.SetSubjectDN(dn);
        certGen.SetSerialNumber(Org.BouncyCastle.Math.BigInteger.ValueOf(DateTime.UtcNow.Ticks));
        certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(keyPair.Public);
        var sigFactory = new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory("SHA256WITHECDSA", keyPair.Private);
        var cert = certGen.Generate(sigFactory);

        string privPem;
        using (var sw = new System.IO.StringWriter())
        {
            var pw = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pw.WriteObject(new Org.BouncyCastle.OpenSsl.Pkcs8Generator(keyPair.Private));
            pw.Writer.Flush();
            privPem = sw.ToString();
        }

        string certPem;
        using (var sw = new System.IO.StringWriter())
        {
            var pw = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pw.WriteObject(cert);
            pw.Writer.Flush();
            certPem = sw.ToString();
        }

        return new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: privPem,
            publicKeyPem: null,
            csrPem: null,
            certificatePem: certPem,
            secret: "secret",
            requestId: "123",
            environment: CsidEnvironment.Simulation,
            isActive: true
        );
    }
}

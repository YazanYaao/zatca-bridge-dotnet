using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Xml.Linq;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.Math;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Cryptography;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class XadesXmlSignerTests
{
    private readonly IXadesXmlSigner _signer;
    private readonly CsidCredential _testCredential;
    private readonly X509Certificate _testCertificate;

    public XadesXmlSignerTests()
    {
        _signer = new XadesXmlSigner(new UblXmlGenerator());

        // Generate test secp256k1 keypair and X.509 certificate using BouncyCastle
        var keyGen = new ECKeyPairGenerator();
        keyGen.Init(new ECKeyGenerationParameters(SecObjectIdentifiers.SecP256k1, new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        var dn = new X509Name("CN=TST-310123456700003, OU=Riyadh, O=Acme Saudi Co, C=SA");
        var certGen = new X509V3CertificateGenerator();
        certGen.SetIssuerDN(dn);
        certGen.SetSubjectDN(dn);
        certGen.SetSerialNumber(BigInteger.ValueOf(DateTime.UtcNow.Ticks));
        certGen.SetNotBefore(DateTime.UtcNow.AddDays(-1));
        certGen.SetNotAfter(DateTime.UtcNow.AddYears(1));
        certGen.SetPublicKey(keyPair.Public);
        var sigFactory = new Org.BouncyCastle.Crypto.Operators.Asn1SignatureFactory("SHA256WITHECDSA", keyPair.Private);
        _testCertificate = certGen.Generate(sigFactory);

        // Export private key to PEM
        string privateKeyPem;
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            var pkcs8 = new Pkcs8Generator(keyPair.Private);
            pemWriter.WriteObject(pkcs8);
            pemWriter.Writer.Flush();
            privateKeyPem = sw.ToString();
        }

        // Export public key to PEM
        string publicKeyPem;
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(keyPair.Public);
            pemWriter.Writer.Flush();
            publicKeyPem = sw.ToString();
        }

        // Export certificate to PEM
        string certPem;
        using (var sw = new StringWriter())
        {
            var pemWriter = new PemWriter(sw);
            pemWriter.WriteObject(_testCertificate);
            pemWriter.Writer.Flush();
            certPem = sw.ToString();
        }

        _testCredential = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: privateKeyPem,
            publicKeyPem: publicKeyPem,
            csrPem: null,
            certificatePem: certPem,
            secret: "csid-secret-xyz",
            requestId: "zatca-req-12345",
            environment: CsidEnvironment.Simulation,
            isActive: true
        );
    }

    private static Invoice CreateTestInvoice(InvoiceTransactionType transactionType = InvoiceTransactionType.B2B)
    {
        var lineItems = new List<InvoiceLineItem>
        {
            new(Guid.NewGuid(), "Professional Consulting", 1.00m, 1000.00m, 0.00m, TaxCategory.Standard),
            new(Guid.NewGuid(), "Software License", 2.00m, 250.00m, 50.00m, TaxCategory.Standard)
        };

        var seller = new Party(
            registrationName: "Acme Saudi Co",
            vatNumber: "310123456700003",
            streetName: "Olaya St",
            buildingNumber: "1234",
            postalCode: "12211",
            city: "Riyadh",
            district: "Olaya",
            countryCode: "SA"
        );

        Party? buyer = transactionType == InvoiceTransactionType.B2B
            ? new Party(
                registrationName: "Retail Partner LLC",
                vatNumber: "320987654300003",
                streetName: "King Fahd Rd",
                buildingNumber: "5678",
                postalCode: "12334",
                city: "Riyadh",
                district: "Nakheel",
                countryCode: "SA")
            : null;

        return new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-2026-XADES-01",
            issueDate: new DateOnly(2026, 11, 17),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: transactionType == InvoiceTransactionType.B2B ? InvoiceTypeCode.TaxInvoice : InvoiceTypeCode.SimplifiedTaxInvoice,
            transactionType: transactionType,
            seller: seller,
            buyer: buyer,
            lineItems: lineItems,
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );
    }

    [Fact]
    public void SignInvoice_ValidInvoiceAndCsid_GeneratesValidHashSignatureAndAll9QrTags()
    {
        // Arrange
        var invoice = CreateTestInvoice(InvoiceTransactionType.B2C);

        // Act
        var result = _signer.SignInvoice(invoice, _testCredential);

        // Assert: 1. Pass 1 Canonical Invoice Hash
        Assert.NotNull(result);
        Assert.False(string.IsNullOrWhiteSpace(result.InvoiceHashBase64));
        var rawHash = Convert.FromBase64String(result.InvoiceHashBase64);
        Assert.Equal(32, rawHash.Length);

        // Assert: 2. Cryptographic Digital Signature
        Assert.False(string.IsNullOrWhiteSpace(result.DigitalSignatureBase64));
        var signatureBytes = Convert.FromBase64String(result.DigitalSignatureBase64);
        Assert.True(signatureBytes.Length > 0);

        // Verify digital signature against public key over invoice hash bytes (per ZATCA SDK)
        var verifier = SignerUtilities.GetSigner("SHA256WITHECDSA");
        verifier.Init(forSigning: false, _testCertificate.GetPublicKey());
        verifier.BlockUpdate(rawHash, 0, rawHash.Length);
        Assert.True(verifier.VerifySignature(signatureBytes), "ECDSA digital signature failed verification against CSID public key.");

        // Assert: 3. Phase 2 QR Code with all 9 TLV Tags
        Assert.False(string.IsNullOrWhiteSpace(result.QrCodeBase64));
        var tags = TlvEncoder.Decode(result.QrCodeBase64);
        Assert.Equal(9, tags.Count);

        // Tag 1 to 5: Standard Metadata
        Assert.Equal(1, tags[0].TagNumber);
        Assert.Equal("Acme Saudi Co", System.Text.Encoding.UTF8.GetString(tags[0].Value));
        Assert.Equal(2, tags[1].TagNumber);
        Assert.Equal("310123456700003", System.Text.Encoding.UTF8.GetString(tags[1].Value));
        Assert.Equal(3, tags[2].TagNumber);
        Assert.Equal(4, tags[3].TagNumber);
        Assert.Equal(5, tags[4].TagNumber);

        // Tag 6: SHA-256 Invoice Hash (UTF-8 bytes of Base64 string, length = 44)
        Assert.Equal(6, tags[5].TagNumber);
        Assert.Equal(44, tags[5].Value.Length);
        Assert.Equal(result.InvoiceHashBase64, System.Text.Encoding.UTF8.GetString(tags[5].Value));

        // Tag 7: ECDSA Digital Signature (UTF-8 bytes of Base64 DER string)
        Assert.Equal(7, tags[6].TagNumber);
        Assert.Equal(result.DigitalSignatureBase64, System.Text.Encoding.UTF8.GetString(tags[6].Value));

        // Tag 8: ECDSA Public Key
        Assert.Equal(8, tags[7].TagNumber);
        Assert.True(tags[7].Value.Length > 0);

        // Tag 9: Certificate Stamp Signature
        Assert.Equal(9, tags[8].TagNumber);
        Assert.True(tags[8].Value.Length > 0);

        // Assert: 4. Pass 2 Enveloped XAdES-BES XML Structure
        Assert.False(string.IsNullOrWhiteSpace(result.SignedXml));
        var xDoc = XDocument.Parse(result.SignedXml);
        Assert.NotNull(xDoc.Root);

        XNamespace nsExt = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
        XNamespace nsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
        XNamespace nsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
        XNamespace nsXades = "http://uri.etsi.org/01903/v1.3.2#";
        XNamespace nsDs = "http://www.w3.org/2000/09/xmldsig#";

        // Must have ext:UBLExtensions as first child
        var ublExtensions = xDoc.Root.Element(nsExt + "UBLExtensions");
        Assert.NotNull(ublExtensions);

        // Must have ds:Signature with SignedInfo, SignatureValue, KeyInfo, and Object
        var signatureElement = xDoc.Descendants(nsDs + "Signature").FirstOrDefault();
        Assert.NotNull(signatureElement);

        var signatureValue = signatureElement.Element(nsDs + "SignatureValue")?.Value;
        Assert.Equal(result.DigitalSignatureBase64, signatureValue);

        var certElement = signatureElement.Descendants(nsDs + "X509Certificate").FirstOrDefault();
        Assert.NotNull(certElement);
        Assert.False(string.IsNullOrWhiteSpace(certElement.Value));

        var xadesQualifyingProps = signatureElement.Descendants(nsXades + "QualifyingProperties").FirstOrDefault();
        Assert.NotNull(xadesQualifyingProps);

        // Must have cac:Signature before supplier party
        var cacSignature = xDoc.Root.Element(nsCac + "Signature");
        Assert.NotNull(cacSignature);

        // Must embed QR code in AdditionalDocumentReference
        var qrRef = xDoc.Descendants(nsCac + "AdditionalDocumentReference")
            .FirstOrDefault(r => r.Element(nsCbc + "ID")?.Value == "QR");
        Assert.NotNull(qrRef);
        var qrValue = qrRef.Descendants(nsCbc + "EmbeddedDocumentBinaryObject").FirstOrDefault()?.Value;
        Assert.Equal(result.QrCodeBase64, qrValue);
    }

    [Fact]
    public void SignInvoice_MissingCertificateOrPrivateKey_ThrowsArgumentException()
    {
        var invoice = CreateTestInvoice();
        var invalidCred = new CsidCredential(
            id: Guid.NewGuid(),
            privateKeyPem: "INVALID-KEY",
            certificatePem: null, // MISSING
            isActive: true
        );

        Assert.ThrowsAny<ArgumentException>(() => _signer.SignInvoice(invoice, invalidCred));
    }
}

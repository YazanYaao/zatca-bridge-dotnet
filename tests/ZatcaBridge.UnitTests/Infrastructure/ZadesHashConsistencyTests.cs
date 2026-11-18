using System;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Xml;
using System.Xml.Linq;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.Enums;
using ZatcaBridge.Domain.ValueObjects;
using ZatcaBridge.Infrastructure.Cryptography;
using ZatcaBridge.Infrastructure.Xml;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class ZadesHashConsistencyTests
{
    private readonly IXadesXmlSigner _signer;
    private readonly CsidCredential _credential;

    public ZadesHashConsistencyTests()
    {
        _signer = new XadesXmlSigner(new UblXmlGenerator());

        // Dummy test key and cert
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
        using (var sw = new StringWriter())
        {
            var pw = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pw.WriteObject(new Org.BouncyCastle.OpenSsl.Pkcs8Generator(keyPair.Private));
            pw.Writer.Flush();
            privPem = sw.ToString();
        }

        string certPem;
        using (var sw = new StringWriter())
        {
            var pw = new Org.BouncyCastle.OpenSsl.PemWriter(sw);
            pw.WriteObject(cert);
            pw.Writer.Flush();
            certPem = sw.ToString();
        }

        _credential = new CsidCredential(
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

    [Fact]
    public void SignedXml_WhenStrippedAndCanonicalized_MustMatchInvoiceHashBase64()
    {
        // Arrange
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-HASH-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2B,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: new Party("Buyer", "310987654300003", "Tahlia", "5678", "12334", "Riyadh", "Sulaimaniyah", "SA"),
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 100, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        // Act
        var result = _signer.SignInvoice(invoice, _credential);

        // Now simulate what ZATCA does on result.SignedXml:
        // 1. Load SignedXml
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(result.SignedXml);

        // 2. Remove ext:UBLExtensions
        var nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsMgr.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        nsMgr.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        nsMgr.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");

        var ublExt = xmlDoc.SelectSingleNode("//ext:UBLExtensions", nsMgr);
        ublExt?.ParentNode?.RemoveChild(ublExt);

        // 3. Remove cac:Signature
        var cacSig = xmlDoc.SelectSingleNode("//cac:Signature", nsMgr);
        cacSig?.ParentNode?.RemoveChild(cacSig);

        // 4. Remove cac:AdditionalDocumentReference[cbc:ID='QR']
        var qrRef = xmlDoc.SelectSingleNode("//cac:AdditionalDocumentReference[cbc:ID='QR']", nsMgr);
        qrRef?.ParentNode?.RemoveChild(qrRef);

        // 5. Canonicalize with C14N
        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(xmlDoc);
        using var ms = (MemoryStream)transform.GetOutput(typeof(Stream));
        var canonicalBytes = ms.ToArray();
        var recomputedHashBase64 = Convert.ToBase64String(SHA256.HashData(canonicalBytes));

        var canonicalStr = System.Text.Encoding.UTF8.GetString(canonicalBytes);
        Assert.True(result.InvoiceHashBase64 == recomputedHashBase64, $"RECOMPUTED CANONICAL:\n{canonicalStr}");

        // Assert: MUST match result.InvoiceHashBase64!
        Assert.Equal(result.InvoiceHashBase64, recomputedHashBase64);
    }

    [Fact]
    public void SignedXml_B2CWithoutBuyer_MustMatchInvoiceHashBase64()
    {
        // Arrange
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-B2C-HASH-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2C,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: null,
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Retail Item", 2, 50, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        // Act
        var result = _signer.SignInvoice(invoice, _credential);

        // Assert: XML must contain AccountingCustomerParty and Party
        Assert.Contains("<cac:AccountingCustomerParty><cac:Party /></cac:AccountingCustomerParty>", result.SignedXml);

        // Now simulate what ZATCA does on result.SignedXml:
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(result.SignedXml);

        var nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsMgr.AddNamespace("ext", "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2");
        nsMgr.AddNamespace("cac", "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2");
        nsMgr.AddNamespace("cbc", "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2");

        var ublExt = xmlDoc.SelectSingleNode("//ext:UBLExtensions", nsMgr);
        ublExt?.ParentNode?.RemoveChild(ublExt);

        var cacSig = xmlDoc.SelectSingleNode("//cac:Signature", nsMgr);
        cacSig?.ParentNode?.RemoveChild(cacSig);

        var qrRef = xmlDoc.SelectSingleNode("//cac:AdditionalDocumentReference[cbc:ID='QR']", nsMgr);
        qrRef?.ParentNode?.RemoveChild(qrRef);

        var transform = new XmlDsigC14NTransform();
        transform.LoadInput(xmlDoc);
        using var ms = (MemoryStream)transform.GetOutput(typeof(Stream));
        var canonicalBytes = ms.ToArray();
        var recomputedHashBase64 = Convert.ToBase64String(SHA256.HashData(canonicalBytes));

        Assert.Equal(result.InvoiceHashBase64, recomputedHashBase64);
    }

    [Fact]
    public void SignedXml_SignedPropertiesDigest_MustBe88CharHexThenBase64()
    {
        // ZATCA REQUIREMENT: SignedProperties DigestValue must be computed as:
        // SHA-256(C14N(signedPropertiesNode)) -> lowercase hex string -> UTF-8 bytes -> Base64
        // Result is always 88 characters (64 hex chars -> UTF-8 -> Base64)
        // Arrange
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-SP-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2B,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: new Party("Buyer", "310987654300003", "Tahlia", "5678", "12334", "Riyadh", "Sulaimaniyah", "SA"),
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 100, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        // Act
        var result = _signer.SignInvoice(invoice, _credential);

        // Parse result.SignedXml
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(result.SignedXml);

        var nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsMgr.AddNamespace("ds", "http://www.w3.org/2000/09/xmldsig#");
        nsMgr.AddNamespace("xades", "http://uri.etsi.org/01903/v1.3.2#");

        // Extract DigestValue from Reference URI="#xadesSignedProperties"
        var refNode = xmlDoc.SelectSingleNode("//ds:Reference[@URI='#xadesSignedProperties']/ds:DigestValue", nsMgr);
        Assert.NotNull(refNode);
        var embeddedDigestValue = refNode.InnerText.Trim();

        // ASSERT 1: Must be 88 characters (Hex-then-Base64 of 64 hex chars)
        Assert.Equal(88, embeddedDigestValue.Length);

        // Extract signing time and cert digest from the XML
        var signingTime = xmlDoc.SelectSingleNode("//xades:SigningTime", nsMgr)?.InnerText;
        var certDigest = xmlDoc.SelectSingleNode("//xades:CertDigest/ds:DigestValue", nsMgr)?.InnerText;
        var issuerName = xmlDoc.SelectSingleNode("//xades:IssuerSerial/ds:X509IssuerName", nsMgr)?.InnerText;
        var serialNumber = xmlDoc.SelectSingleNode("//xades:IssuerSerial/ds:X509SerialNumber", nsMgr)?.InnerText;

        // Reconstruct the exact hashing template matching ZATCA SDK specification
        var template = XadesXmlSigner.BuildSignedPropertiesForHashing(
            signingTime!,
            certDigest!,
            issuerName!,
            serialNumber!
        );

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(template));
        var hexStr = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var expectedHexThenBase64 = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(hexStr));

        Assert.Equal(88, expectedHexThenBase64.Length);
        Assert.Equal(expectedHexThenBase64, embeddedDigestValue);
    }

    [Fact]
    public void SignedProperties_OfficialZatcaSample_MatchesExactDigest()
    {
        // Verified against official ZATCA SDK Java sample (sample_simplified.xml line 32)
        var signingTime = "2025-07-22T15:51:28";
        var certHash = "ZDMwMmI0MTE1NzVjOTU2NTk4YzVlODhhYmI0ODU2NDUyNTU2YTVhYjhhMDFmN2FjYjk1YTA2OWQ0NjY2MjQ4NQ==";
        var issuer = "CN=PRZEINVOICESCA4-CA, DC=extgazt, DC=gov, DC=local";
        var serial = "379112742831380471835263969587287663520528387";

        var template = XadesXmlSigner.BuildSignedPropertiesForHashing(signingTime, certHash, issuer, serial);

        var hashBytes = SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(template));
        var hex = Convert.ToHexString(hashBytes).ToLowerInvariant();
        var base64OfHex = Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(hex));

        Assert.Equal("ZmMwY2ZhNDljNzNjZDA5NmY4NDM4MmY1ZmY1YTA0NjY3MzY4NzMxOGJhYmZmNWU1OGYzZWJlODI3ZDgyZGVkZA==", base64OfHex);
    }

    [Fact]
    public void QrTag3_MustMatchIssueDateAndTime_WithoutTimezoneConversion()
    {
        // ZATCA REQUIREMENT: Tag 3 timestamp must be YYYY-MM-DDTHH:mm:ss (no Z, no UTC conversion)
        var issueDate = new DateOnly(2026, 6, 15);
        var issueTime = new TimeOnly(14, 30, 45);
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-QR3-TEST",
            issueDate: issueDate,
            issueTime: issueTime,
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2C,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: null,
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 50, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var result = _signer.SignInvoice(invoice, _credential);
        var qrBytes = Convert.FromBase64String(result.QrCodeBase64);
        var parsedTags = ZatcaBridge.Domain.Cryptography.TlvEncoder.Decode(qrBytes).ToDictionary(t => t.TagNumber, t => t.Value);

        Assert.True(parsedTags.ContainsKey(3), "QR must contain Tag 3 (Timestamp).");
        var tag3Str = System.Text.Encoding.UTF8.GetString(parsedTags[3]);

        // Must match YYYY-MM-DDTHH:mm:ss exactly (no Z suffix)
        Assert.Equal("2026-06-15T14:30:45", tag3Str);
    }

    [Fact]
    public void QrTag6_MustBeUtf8OfBase64HashString_Length44()
    {
        // ZATCA REQUIREMENT: Tag 6 must store the UTF-8 bytes of the 44-char Base64 invoice hash string
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-QR6-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2C,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: null,
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 100, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var result = _signer.SignInvoice(invoice, _credential);
        var qrBytes = Convert.FromBase64String(result.QrCodeBase64);
        var parsedTags = ZatcaBridge.Domain.Cryptography.TlvEncoder.Decode(qrBytes).ToDictionary(t => t.TagNumber, t => t.Value);

        Assert.True(parsedTags.ContainsKey(6), "QR must contain Tag 6 (Invoice Hash).");
        // Tag 6 value must be UTF-8 bytes of the base64 string (length = 44 bytes for standard SHA-256 base64)
        Assert.Equal(44, parsedTags[6].Length);
        var tag6Str = System.Text.Encoding.UTF8.GetString(parsedTags[6]);
        Assert.Equal(result.InvoiceHashBase64, tag6Str);
    }

    [Fact]
    public void QrTag7_MustBeUtf8OfBase64SignatureString()
    {
        // ZATCA REQUIREMENT: Tag 7 must store the UTF-8 bytes of the Base64 DER signature string (not raw bytes)
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-QR7-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2C,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: null,
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 100, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var result = _signer.SignInvoice(invoice, _credential);
        var qrBytes = Convert.FromBase64String(result.QrCodeBase64);
        var parsedTags = ZatcaBridge.Domain.Cryptography.TlvEncoder.Decode(qrBytes).ToDictionary(t => t.TagNumber, t => t.Value);

        Assert.True(parsedTags.ContainsKey(7), "QR must contain Tag 7 (Digital Signature).");
        // Tag 7 value is UTF-8 bytes of Base64 DER string; decode to get the base64 string back
        var tag7Str = System.Text.Encoding.UTF8.GetString(parsedTags[7]);
        Assert.Equal(result.DigitalSignatureBase64, tag7Str);
    }

    [Fact]
    public void SigningTime_InSignedProperties_MustNotHaveZSuffix()
    {
        // ZATCA REQUIREMENT: SigningTime must be YYYY-MM-DDTHH:mm:ss (no Z suffix)
        var invoice = new Invoice(
            id: Guid.NewGuid(),
            uuid: Guid.NewGuid(),
            invoiceNumber: "INV-SIGTIME-TEST",
            issueDate: new DateOnly(2026, 11, 18),
            issueTime: new TimeOnly(12, 0, 0),
            invoiceType: InvoiceTypeCode.TaxInvoice,
            transactionType: InvoiceTransactionType.B2B,
            seller: new Party("Acme", "310123456700003", "Olaya", "1234", "12211", "Riyadh", "Olaya", "SA"),
            buyer: new Party("Buyer", "310987654300003", "Tahlia", "5678", "12334", "Riyadh", "Sulaimaniyah", "SA"),
            lineItems: new[] { new InvoiceLineItem(Guid.NewGuid(), "Item", 1, 100, 0, TaxCategory.Standard) },
            invoiceCounterValue: 1,
            previousInvoiceHash: PreviousInvoiceHash.Genesis.Value
        );

        var result = _signer.SignInvoice(invoice, _credential);

        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        xmlDoc.LoadXml(result.SignedXml);
        var nsMgr = new XmlNamespaceManager(xmlDoc.NameTable);
        nsMgr.AddNamespace("xades", "http://uri.etsi.org/01903/v1.3.2#");

        var signingTimeNode = xmlDoc.SelectSingleNode("//xades:SigningTime", nsMgr);
        Assert.NotNull(signingTimeNode);
        var signingTimeStr = signingTimeNode.InnerText;

        // Must NOT end with 'Z'
        Assert.False(signingTimeStr.EndsWith("Z"), $"SigningTime '{signingTimeStr}' must not have a Z suffix per ZATCA requirements.");
        // Must match yyyy-MM-ddTHH:mm:ss pattern (19 chars)
        Assert.Equal(19, signingTimeStr.Length);
    }

    [Fact]
    public void Test_ActiveCertificateBytes_Comparison()
    {
        var dbPath = Path.GetFullPath("src/ZatcaBridge.Api/zatca_bridge.db");
        if (!File.Exists(dbPath))
        {
            dbPath = Path.GetFullPath("../../../../../src/ZatcaBridge.Api/zatca_bridge.db");
        }
        if (!File.Exists(dbPath))
        {
            dbPath = Path.GetFullPath("src/ZatcaBridge.Api/zatcabridge.db");
        }
        if (!File.Exists(dbPath))
        {
            dbPath = Path.GetFullPath("../../../../../src/ZatcaBridge.Api/zatcabridge.db");
        }
        if (!File.Exists(dbPath))
        {
            // If local development DB doesn't exist yet, skip this environment test
            return;
        }

        using var conn = new Microsoft.Data.Sqlite.SqliteConnection($"Data Source={dbPath}");
        conn.Open();
        using var cmd = conn.CreateCommand();
        cmd.CommandText = "SELECT CertificatePem FROM CsidCredentials WHERE IsActive = 1 LIMIT 1";
        var pemObj = cmd.ExecuteScalar();
        if (pemObj is not string certPem) return;

        var clean = certPem
            .Replace("-----BEGIN CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-----END CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        var directBytes = Convert.FromBase64String(clean);
        var utf8Str = System.Text.Encoding.UTF8.GetString(directBytes);
        byte[] originalDerBytes;
        if (utf8Str.StartsWith("MII", StringComparison.OrdinalIgnoreCase))
        {
            originalDerBytes = Convert.FromBase64String(utf8Str);
        }
        else
        {
            originalDerBytes = directBytes;
        }

        // Now parse via BouncyCastle
        var parser = new Org.BouncyCastle.X509.X509CertificateParser();
        var bcCert = parser.ReadCertificate(originalDerBytes);
        var bcBytes = bcCert.GetEncoded();

        var origHash = Convert.ToHexString(SHA256.HashData(originalDerBytes)).ToLowerInvariant();
        var bcHash = Convert.ToHexString(SHA256.HashData(bcBytes)).ToLowerInvariant();

        // Check hash of base64 string
        var cleanBase64 = Convert.ToBase64String(originalDerBytes);
        var base64BytesHash = Convert.ToHexString(SHA256.HashData(System.Text.Encoding.UTF8.GetBytes(cleanBase64))).ToLowerInvariant();

        Assert.Equal(origHash, bcHash);
        Assert.Equal("d302b411575c956598c5e88abb4856452556a5ab8a01f7acb95a069d46662485", base64BytesHash);
    }
}

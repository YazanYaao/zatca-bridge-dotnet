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
}

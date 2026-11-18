using System;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Security.Cryptography;
using System.Security.Cryptography.Xml;
using System.Text;
using System.Xml;
using System.Xml.Linq;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.X509;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Infrastructure.Cryptography;

/// <summary>
/// Implements ZATCA Phase 2 two-pass canonicalization, XAdES-BES digital signature injection,
/// and Phase 2 QR TLV Tags 1–9 encoding.
/// </summary>
public class XadesXmlSigner : IXadesXmlSigner
{
    private static readonly XNamespace NsInvoice = "urn:oasis:names:specification:ubl:schema:xsd:Invoice-2";
    private static readonly XNamespace NsCac = "urn:oasis:names:specification:ubl:schema:xsd:CommonAggregateComponents-2";
    private static readonly XNamespace NsCbc = "urn:oasis:names:specification:ubl:schema:xsd:CommonBasicComponents-2";
    private static readonly XNamespace NsExt = "urn:oasis:names:specification:ubl:schema:xsd:CommonExtensionComponents-2";
    private static readonly XNamespace NsSig = "urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2";
    private static readonly XNamespace NsSac = "urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2";
    private static readonly XNamespace NsSbc = "urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2";
    private static readonly XNamespace NsDs = "http://www.w3.org/2000/09/xmldsig#";
    private static readonly XNamespace NsXades = "http://uri.etsi.org/01903/v1.3.2#";

    private readonly IZatcaXmlGenerator _xmlGenerator;

    public XadesXmlSigner(IZatcaXmlGenerator xmlGenerator)
    {
        _xmlGenerator = xmlGenerator ?? throw new ArgumentNullException(nameof(xmlGenerator));
    }

    public XadesSigningResult SignInvoice(Invoice invoice, CsidCredential credential)
    {
        ArgumentNullException.ThrowIfNull(invoice);
        ArgumentNullException.ThrowIfNull(credential);

        if (string.IsNullOrWhiteSpace(credential.PrivateKeyPem))
        {
            throw new ArgumentException("CSID private key PEM cannot be null or whitespace.", nameof(credential));
        }

        if (string.IsNullOrWhiteSpace(credential.CertificatePem))
        {
            throw new ArgumentException("CSID certificate PEM cannot be null or whitespace.", nameof(credential));
        }

        // 1. Parse Private Key and Certificate via BouncyCastle
        var privateKey = ParsePrivateKey(credential.PrivateKeyPem);
        var certificate = ParseCertificate(credential.CertificatePem);

        // 2. Generate Base UBL XML and strip excluded nodes for Pass 1 Canonicalization
        var baseXml = _xmlGenerator.GenerateXml(invoice, qrCodeBase64: null);
        var xDoc = XDocument.Parse(baseXml);

        // Strip excluded nodes from pre-signed document for hashing
        xDoc.Root?.Element(NsExt + "UBLExtensions")?.Remove();
        xDoc.Root?.Element(NsCac + "Signature")?.Remove();
        xDoc.Descendants(NsCac + "AdditionalDocumentReference")
            .Where(e => e.Element(NsCbc + "ID")?.Value == "QR")
            .Remove();

        // 3. Pass 1: Compute Canonical Invoice SHA-256 Hash
        var canonicalBytes = CanonicalizeXml(xDoc);
        var invoiceHashBytes = SHA256.HashData(canonicalBytes);
        var invoiceHashBase64 = Convert.ToBase64String(invoiceHashBytes);

        // 4. Extract Certificate Details & Public Key
        var certBytes = certificate.GetEncoded();
        var certBase64 = Convert.ToBase64String(certBytes);
        // ZATCA requirement: CertDigest is the SHA-256 of the Base64-encoded certificate string, formatted as lowercase hex then base64 encoded
        var certBase64Bytes = Encoding.UTF8.GetBytes(certBase64);
        var certHashBytes = SHA256.HashData(certBase64Bytes);
        var certHashHex = Convert.ToHexString(certHashBytes).ToLowerInvariant();
        var certDigestBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(certHashHex));

        using var x509 = System.Security.Cryptography.X509Certificates.X509CertificateLoader.LoadCertificate(certBytes);
        var issuerName = x509.Issuer;

        // 5. Pass 2: Enveloped XAdES-BES SignedProperties Construction
        // ZATCA REQUIREMENT: No 'Z' suffix on signing time (YYYY-MM-DDTHH:mm:ss)
        var signingTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture);

        // ZATCA REQUIREMENT: SignedProperties digest must be computed from the exact template
        // with 36-space child indentation and xmlns:ds declared on each ds: element,
        // using Hex-then-Base64 encoding (SHA-256 -> lowercase hex -> UTF-8 -> Base64 = 88 chars).
        var spHashingTemplate = BuildSignedPropertiesForHashing(
            signingTime,
            certDigestBase64,
            issuerName,
            certificate.SerialNumber.ToString()
        );

        var spHashBytes = SHA256.HashData(Encoding.UTF8.GetBytes(spHashingTemplate));
        var spHashHex = Convert.ToHexString(spHashBytes).ToLowerInvariant();
        var signedPropertiesHashBase64 = Convert.ToBase64String(Encoding.UTF8.GetBytes(spHashHex));

        // 6. Sign invoice hash using ECDSA secp256k1 (SHA256WITHECDSA)
        // ZATCA REQUIREMENT: Sign the invoice hash bytes directly (SHA256WITHECDSA will internally SHA-256 them)
        var signer = SignerUtilities.GetSigner("SHA256WITHECDSA");
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(invoiceHashBytes, 0, invoiceHashBytes.Length);
        var digitalSignatureBytes = signer.GenerateSignature();
        var digitalSignatureBase64 = Convert.ToBase64String(digitalSignatureBytes);

        // 7. Extract Public Key & CA Signature for Phase 2 QR Code with all 9 TLV Tags
        var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(certificate.GetPublicKey());
        var publicKeyBytes = publicKeyInfo.GetDerEncoded();
        var certSignatureBytes = certificate.GetSignature();

        var qrData = ZatcaQrCodeData.FromInvoice(
            invoice,
            invoiceHashBase64: invoiceHashBase64,
            digitalSignatureBase64: digitalSignatureBase64,
            publicKey: publicKeyBytes,
            certificateSignature: certSignatureBytes
        );
        var qrCodeBase64 = TlvEncoder.EncodeToBase64(qrData);

        // 8. Add cac:Signature & QR DocumentReference to xDoc
        var cacSignature = new XElement(NsCac + "Signature",
            new XElement(NsCbc + "ID", "urn:oasis:names:specification:ubl:signature:Invoice"),
            new XElement(NsCbc + "SignatureMethod", "urn:oasis:names:specification:ubl:dsig:enveloped:xades")
        );

        var qrDocumentRef = new XElement(NsCac + "AdditionalDocumentReference",
            new XElement(NsCbc + "ID", "QR"),
            new XElement(NsCac + "Attachment",
                new XElement(NsCbc + "EmbeddedDocumentBinaryObject",
                    new XAttribute("mimeCode", "text/plain"),
                    qrCodeBase64
                )
            )
        );

        // Add QR Document Reference
        var pihRef = xDoc.Descendants(NsCac + "AdditionalDocumentReference")
            .FirstOrDefault(r => r.Element(NsCbc + "ID")?.Value == "PIH");
        if (pihRef != null)
        {
            pihRef.AddAfterSelf(qrDocumentRef);
        }
        else
        {
            var icvRef = xDoc.Descendants(NsCac + "AdditionalDocumentReference")
                .FirstOrDefault(r => r.Element(NsCbc + "ID")?.Value == "ICV");
            if (icvRef != null)
            {
                icvRef.AddAfterSelf(qrDocumentRef);
            }
            else
            {
                xDoc.Root!.Add(qrDocumentRef);
            }
        }

        // Add cac:Signature before AccountingSupplierParty
        var supplierParty = xDoc.Root!.Element(NsCac + "AccountingSupplierParty");
        if (supplierParty != null)
        {
            supplierParty.AddBeforeSelf(cacSignature);
        }
        else
        {
            xDoc.Root.Add(cacSignature);
        }

        // Export document with cac:Signature and QR
        var docXml = ExportXDocumentToString(xDoc);

        // 9. Build byte-exact UBLExtensions envelope and insert into XML string (preserving whitespace)
        var ublExtensions = BuildUblExtensions(
            invoiceHashBase64,
            signedPropertiesHashBase64,
            digitalSignatureBase64,
            certBase64,
            signingTime,
            certDigestBase64,
            issuerName,
            certificate.SerialNumber.ToString()
        );

        var invoiceMatch = System.Text.RegularExpressions.Regex.Match(docXml, @"<Invoice[^>]*>");
        if (!invoiceMatch.Success)
        {
            throw new InvalidOperationException("Failed to find <Invoice> root in XML.");
        }

        var signedXml = docXml.Insert(invoiceMatch.Index + invoiceMatch.Length, ublExtensions);
        return new XadesSigningResult(signedXml, invoiceHashBase64, digitalSignatureBase64, qrCodeBase64);
    }

    private static byte[] CanonicalizeXml(XDocument doc)
    {
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        using (var reader = doc.CreateReader())
        {
            xmlDoc.Load(reader);
        }

        var transform = new XmlDsigC14NTransform(includeComments: false);
        transform.LoadInput(xmlDoc);
        using var stream = (MemoryStream)transform.GetOutput(typeof(Stream));
        return NormalizeUnixLineEndings(stream.ToArray());
    }

    private static byte[] CanonicalizeElement(XElement element)
    {
        var xmlDoc = new XmlDocument { PreserveWhitespace = true };
        using (var reader = element.CreateReader())
        {
            xmlDoc.Load(reader);
        }

        var transform = new XmlDsigC14NTransform(includeComments: false);
        transform.LoadInput(xmlDoc);
        using var stream = (MemoryStream)transform.GetOutput(typeof(Stream));
        return NormalizeUnixLineEndings(stream.ToArray());
    }

    private static byte[] NormalizeUnixLineEndings(byte[] bytes)
    {
        var str = Encoding.UTF8.GetString(bytes).Replace("\r\n", "\n").Replace("\r", "\n");
        return Encoding.UTF8.GetBytes(str);
    }

    private static string ExportXDocumentToString(XDocument doc)
    {
        var settings = new XmlWriterSettings
        {
            Encoding = new UTF8Encoding(false),
            Indent = false,
            NewLineChars = "\n",
            OmitXmlDeclaration = false
        };

        using var memoryStream = new MemoryStream();
        using (var writer = XmlWriter.Create(memoryStream, settings))
        {
            doc.Save(writer);
        }

        return new UTF8Encoding(false).GetString(memoryStream.ToArray());
    }

    private static ECPrivateKeyParameters ParsePrivateKey(string pem)
    {
        using var reader = new StringReader(pem);
        var pemReader = new PemReader(reader);
        var obj = pemReader.ReadObject();

        if (obj is AsymmetricCipherKeyPair ackp)
        {
            return (ECPrivateKeyParameters)ackp.Private;
        }

        if (obj is ECPrivateKeyParameters ecpk)
        {
            return ecpk;
        }

        throw new ArgumentException("Failed to extract secp256k1 EC private key from PEM string.");
    }

    private static X509Certificate ParseCertificate(string pem)
    {
        try
        {
            using var reader = new StringReader(pem);
            var pemReader = new PemReader(reader);
            var obj = pemReader.ReadObject();

            if (obj is X509Certificate cert)
            {
                return cert;
            }
        }
        catch
        {
            // If PemReader throws due to nested Base64 encoding (e.g. ZATCA binarySecurityToken), handle below
        }

        var clean = pem
            .Replace("-----BEGIN CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("-----END CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
            .Replace("\r", "")
            .Replace("\n", "")
            .Trim();

        try
        {
            var bytes = Convert.FromBase64String(clean);
            var utf8 = Encoding.UTF8.GetString(bytes);

            // If the decoded content is ASCII Base64 certificate starting with "MII"
            if (utf8.StartsWith("MII", StringComparison.OrdinalIgnoreCase))
            {
                var der = Convert.FromBase64String(utf8);
                var parser = new X509CertificateParser();
                return parser.ReadCertificate(der);
            }
            else
            {
                var parser = new X509CertificateParser();
                return parser.ReadCertificate(bytes);
            }
        }
        catch (Exception ex)
        {
            throw new ArgumentException("Failed to extract X.509 certificate from PEM string.", ex);
        }
    }

    /// <summary>
    /// Builds the exact byte-for-byte SignedProperties XML snippet used for ZATCA digest hashing.
    /// Notice: 36-space child indentation and xmlns:ds declared on each ds: child element.
    /// </summary>
    public static string BuildSignedPropertiesForHashing(
        string signingTime,
        string certDigestBase64,
        string issuerName,
        string serialNumber)
    {
        var sb = new StringBuilder();
        sb.Append("<xades:SignedProperties xmlns:xades=\"http://uri.etsi.org/01903/v1.3.2#\" Id=\"xadesSignedProperties\">\n");
        sb.Append("                                    <xades:SignedSignatureProperties>\n");
        sb.Append($"                                        <xades:SigningTime>{signingTime}</xades:SigningTime>\n");
        sb.Append("                                        <xades:SigningCertificate>\n");
        sb.Append("                                            <xades:Cert>\n");
        sb.Append("                                                <xades:CertDigest>\n");
        sb.Append("                                                    <ds:DigestMethod xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\" Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>\n");
        sb.Append($"                                                    <ds:DigestValue xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">{certDigestBase64}</ds:DigestValue>\n");
        sb.Append("                                                </xades:CertDigest>\n");
        sb.Append("                                                <xades:IssuerSerial>\n");
        sb.Append($"                                                    <ds:X509IssuerName xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">{issuerName}</ds:X509IssuerName>\n");
        sb.Append($"                                                    <ds:X509SerialNumber xmlns:ds=\"http://www.w3.org/2000/09/xmldsig#\">{serialNumber}</ds:X509SerialNumber>\n");
        sb.Append("                                                </xades:IssuerSerial>\n");
        sb.Append("                                            </xades:Cert>\n");
        sb.Append("                                        </xades:SigningCertificate>\n");
        sb.Append("                                    </xades:SignedSignatureProperties>\n");
        sb.Append("                                </xades:SignedProperties>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the exact byte-for-byte SignedProperties XML snippet as embedded in the signed XML document.
    /// Notice: 36-space child indentation, namespaces inherited from ancestors.
    /// </summary>
    public static string BuildSignedPropertiesForXml(
        string signingTime,
        string certDigestBase64,
        string issuerName,
        string serialNumber)
    {
        var sb = new StringBuilder();
        sb.Append("<xades:SignedProperties Id=\"xadesSignedProperties\">\n");
        sb.Append("                                    <xades:SignedSignatureProperties>\n");
        sb.Append($"                                        <xades:SigningTime>{signingTime}</xades:SigningTime>\n");
        sb.Append("                                        <xades:SigningCertificate>\n");
        sb.Append("                                            <xades:Cert>\n");
        sb.Append("                                                <xades:CertDigest>\n");
        sb.Append("                                                    <ds:DigestMethod Algorithm=\"http://www.w3.org/2001/04/xmlenc#sha256\"/>\n");
        sb.Append($"                                                    <ds:DigestValue>{certDigestBase64}</ds:DigestValue>\n");
        sb.Append("                                                </xades:CertDigest>\n");
        sb.Append("                                                <xades:IssuerSerial>\n");
        sb.Append($"                                                    <ds:X509IssuerName>{issuerName}</ds:X509IssuerName>\n");
        sb.Append($"                                                    <ds:X509SerialNumber>{serialNumber}</ds:X509SerialNumber>\n");
        sb.Append("                                                </xades:IssuerSerial>\n");
        sb.Append("                                            </xades:Cert>\n");
        sb.Append("                                        </xades:SigningCertificate>\n");
        sb.Append("                                    </xades:SignedSignatureProperties>\n");
        sb.Append("                                </xades:SignedProperties>");
        return sb.ToString();
    }

    /// <summary>
    /// Builds the complete byte-exact UBLExtensions signature block for insertion into the document.
    /// </summary>
    public static string BuildUblExtensions(
        string invoiceHashBase64,
        string signedPropertiesHashBase64,
        string digitalSignatureBase64,
        string certBase64,
        string signingTime,
        string certDigestBase64,
        string issuerName,
        string serialNumber)
    {
        var spXml = BuildSignedPropertiesForXml(signingTime, certDigestBase64, issuerName, serialNumber);
        return
$@"<ext:UBLExtensions>
    <ext:UBLExtension>
        <ext:ExtensionURI>urn:oasis:names:specification:ubl:dsig:enveloped:xades</ext:ExtensionURI>
        <ext:ExtensionContent>
            <sig:UBLDocumentSignatures xmlns:sig=""urn:oasis:names:specification:ubl:schema:xsd:CommonSignatureComponents-2"" xmlns:sac=""urn:oasis:names:specification:ubl:schema:xsd:SignatureAggregateComponents-2"" xmlns:sbc=""urn:oasis:names:specification:ubl:schema:xsd:SignatureBasicComponents-2"">
                <sac:SignatureInformation> 
                    <cbc:ID>urn:oasis:names:specification:ubl:signature:1</cbc:ID>
                    <sbc:ReferencedSignatureID>urn:oasis:names:specification:ubl:signature:Invoice</sbc:ReferencedSignatureID>
                    <ds:Signature xmlns:ds=""http://www.w3.org/2000/09/xmldsig#"" Id=""signature"">
                        <ds:SignedInfo>
                            <ds:CanonicalizationMethod Algorithm=""http://www.w3.org/2006/12/xml-c14n11""/>
                            <ds:SignatureMethod Algorithm=""http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256""/>
                            <ds:Reference Id=""invoiceSignedData"" URI="""">
                                <ds:Transforms>
                                    <ds:Transform Algorithm=""http://www.w3.org/TR/1999/REC-xpath-19991116"">
                                        <ds:XPath>not(//ancestor-or-self::ext:UBLExtensions)</ds:XPath>
                                    </ds:Transform>
                                    <ds:Transform Algorithm=""http://www.w3.org/TR/1999/REC-xpath-19991116"">
                                        <ds:XPath>not(//ancestor-or-self::cac:Signature)</ds:XPath>
                                    </ds:Transform>
                                    <ds:Transform Algorithm=""http://www.w3.org/TR/1999/REC-xpath-19991116"">
                                        <ds:XPath>not(//ancestor-or-self::cac:AdditionalDocumentReference[cbc:ID='QR'])</ds:XPath>
                                    </ds:Transform>
                                    <ds:Transform Algorithm=""http://www.w3.org/2006/12/xml-c14n11""/>
                                </ds:Transforms>
                                <ds:DigestMethod Algorithm=""http://www.w3.org/2001/04/xmlenc#sha256""/>
                                <ds:DigestValue>{invoiceHashBase64}</ds:DigestValue>
                            </ds:Reference>
                            <ds:Reference Type=""http://www.w3.org/2000/09/xmldsig#SignatureProperties"" URI=""#xadesSignedProperties"">
                                <ds:DigestMethod Algorithm=""http://www.w3.org/2001/04/xmlenc#sha256""/>
                                <ds:DigestValue>{signedPropertiesHashBase64}</ds:DigestValue>
                            </ds:Reference>
                        </ds:SignedInfo>
                        <ds:SignatureValue>{digitalSignatureBase64}</ds:SignatureValue>
                        <ds:KeyInfo>
                            <ds:X509Data>
                                <ds:X509Certificate>{certBase64}</ds:X509Certificate>
                            </ds:X509Data>
                        </ds:KeyInfo>
                        <ds:Object>
                            <xades:QualifyingProperties xmlns:xades=""http://uri.etsi.org/01903/v1.3.2#"" Target=""signature"">
                                {spXml}
                            </xades:QualifyingProperties>
                        </ds:Object>
                    </ds:Signature>
                </sac:SignatureInformation>
            </sig:UBLDocumentSignatures>
        </ext:ExtensionContent>
    </ext:UBLExtension>
</ext:UBLExtensions>".Replace("\r\n", "\n").Replace("\r", "\n");
    }
}

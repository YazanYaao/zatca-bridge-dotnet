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

        // 4. Sign Invoice Hash using ECDSA secp256k1 (SHA256WITHECDSA)
        var signer = SignerUtilities.GetSigner("SHA256WITHECDSA");
        signer.Init(forSigning: true, privateKey);
        signer.BlockUpdate(invoiceHashBytes, 0, invoiceHashBytes.Length);
        var digitalSignatureBytes = signer.GenerateSignature();
        var digitalSignatureBase64 = Convert.ToBase64String(digitalSignatureBytes);

        // 5. Extract Certificate Details & Public Key for Tags 8 & 9
        var certBytes = certificate.GetEncoded();
        var certBase64 = Convert.ToBase64String(certBytes);
        var certHashBytes = SHA256.HashData(certBytes);
        var certHashBase64 = Convert.ToBase64String(certHashBytes);

        var publicKeyInfo = SubjectPublicKeyInfoFactory.CreateSubjectPublicKeyInfo(certificate.GetPublicKey());
        var publicKeyBytes = publicKeyInfo.GetDerEncoded();
        var certSignatureBytes = certificate.GetSignature();

        // 6. Encode Phase 2 QR Code with all 9 TLV Tags
        var qrData = ZatcaQrCodeData.FromInvoice(
            invoice,
            invoiceHash: invoiceHashBytes,
            digitalSignature: digitalSignatureBytes,
            publicKey: publicKeyBytes,
            certificateSignature: certSignatureBytes
        );
        var qrCodeBase64 = TlvEncoder.EncodeToBase64(qrData);

        // 7. Pass 2: Enveloped XAdES-BES XML Construction
        var signingTime = DateTime.UtcNow.ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);

        var signedProperties = new XElement(NsXades + "SignedProperties",
            new XAttribute("Id", "xadesSignedProperties"),
            new XElement(NsXades + "SignedSignatureProperties",
                new XElement(NsXades + "SigningTime", signingTime),
                new XElement(NsXades + "SigningCertificate",
                    new XElement(NsXades + "Cert",
                        new XElement(NsXades + "CertDigest",
                            new XElement(NsDs + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                            new XElement(NsDs + "DigestValue", certHashBase64)
                        ),
                        new XElement(NsXades + "IssuerSerial",
                            new XElement(NsDs + "X509IssuerName", certificate.IssuerDN.ToString()),
                            new XElement(NsDs + "X509SerialNumber", certificate.SerialNumber.ToString())
                        )
                    )
                )
            )
        );

        var signedPropertiesBytes = CanonicalizeElement(signedProperties);
        var signedPropertiesHashBase64 = Convert.ToBase64String(SHA256.HashData(signedPropertiesBytes));

        var dsSignature = new XElement(NsDs + "Signature",
            new XAttribute(XNamespace.Xmlns + "ds", NsDs.NamespaceName),
            new XAttribute("Id", "signature"),
            new XElement(NsDs + "SignedInfo",
                new XElement(NsDs + "CanonicalizationMethod", new XAttribute("Algorithm", "http://www.w3.org/2006/12/xml-c14n11")),
                new XElement(NsDs + "SignatureMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmldsig-more#ecdsa-sha256")),
                new XElement(NsDs + "Reference",
                    new XAttribute("Id", "invoiceSignedData"),
                    new XAttribute("URI", ""),
                    new XElement(NsDs + "Transforms",
                        new XElement(NsDs + "Transform",
                            new XAttribute("Algorithm", "http://www.w3.org/TR/1999/REC-xpath-19991116"),
                            new XElement(NsDs + "XPath", "not(//ancestor-or-self::ext:UBLExtensions)")
                        ),
                        new XElement(NsDs + "Transform",
                            new XAttribute("Algorithm", "http://www.w3.org/TR/1999/REC-xpath-19991116"),
                            new XElement(NsDs + "XPath", "not(//ancestor-or-self::cac:Signature)")
                        ),
                        new XElement(NsDs + "Transform",
                            new XAttribute("Algorithm", "http://www.w3.org/TR/1999/REC-xpath-19991116"),
                            new XElement(NsDs + "XPath", "not(//ancestor-or-self::cac:AdditionalDocumentReference[cbc:ID='QR'])")
                        ),
                        new XElement(NsDs + "Transform",
                            new XAttribute("Algorithm", "http://www.w3.org/2006/12/xml-c14n11")
                        )
                    ),
                    new XElement(NsDs + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                    new XElement(NsDs + "DigestValue", invoiceHashBase64)
                ),
                new XElement(NsDs + "Reference",
                    new XAttribute("Type", "http://www.w3.org/2000/09/xmldsig#SignatureProperties"),
                    new XAttribute("URI", "#xadesSignedProperties"),
                    new XElement(NsDs + "DigestMethod", new XAttribute("Algorithm", "http://www.w3.org/2001/04/xmlenc#sha256")),
                    new XElement(NsDs + "DigestValue", signedPropertiesHashBase64)
                )
            ),
            new XElement(NsDs + "SignatureValue", digitalSignatureBase64),
            new XElement(NsDs + "KeyInfo",
                new XElement(NsDs + "X509Data",
                    new XElement(NsDs + "X509Certificate", certBase64)
                )
            ),
            new XElement(NsDs + "Object",
                new XElement(NsXades + "QualifyingProperties",
                    new XAttribute(XNamespace.Xmlns + "xades", NsXades.NamespaceName),
                    new XAttribute("Target", "signature"),
                    signedProperties
                )
            )
        );

        var ublExtensions = new XElement(NsExt + "UBLExtensions",
            new XElement(NsExt + "UBLExtension",
                new XElement(NsExt + "ExtensionURI", "urn:oasis:names:specification:ubl:dsig:enveloped:xades"),
                new XElement(NsExt + "ExtensionContent",
                    new XElement(NsSig + "UBLDocumentSignatures",
                        new XAttribute(XNamespace.Xmlns + "sig", NsSig.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "sac", NsSac.NamespaceName),
                        new XAttribute(XNamespace.Xmlns + "sbc", NsSbc.NamespaceName),
                        new XElement(NsSac + "SignatureInformation",
                            new XElement(NsCbc + "ID", "urn:oasis:names:specification:ubl:signature:1"),
                            new XElement(NsSbc + "ReferencedSignatureID", "urn:oasis:names:specification:ubl:signature:Invoice"),
                            dsSignature
                        )
                    )
                )
            )
        );

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

        // Inject ublExtensions as first child of <Invoice>
        xDoc.Root!.AddFirst(ublExtensions);

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
                xDoc.Root.Add(qrDocumentRef);
            }
        }

        // Add cac:Signature before AccountingSupplierParty
        var supplierParty = xDoc.Root.Element(NsCac + "AccountingSupplierParty");
        if (supplierParty != null)
        {
            supplierParty.AddBeforeSelf(cacSignature);
        }
        else
        {
            xDoc.Root.Add(cacSignature);
        }

        var signedXml = ExportXDocumentToString(xDoc);
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
        return stream.ToArray();
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
        return stream.ToArray();
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
}

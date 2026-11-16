using System;
using System.Collections.Generic;
using System.IO;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.Sec;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.Crypto;
using Org.BouncyCastle.Crypto.Generators;
using Org.BouncyCastle.Crypto.Operators;
using Org.BouncyCastle.Crypto.Parameters;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Org.BouncyCastle.Security;
using Org.BouncyCastle.Utilities.IO.Pem;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;

namespace ZatcaBridge.Infrastructure.Cryptography;

/// <summary>
/// Cryptographic service that generates ECDSA secp256k1 keypairs and ZATCA-compliant PKCS#10 Certificate Signing Requests.
/// </summary>
public class ZatcaCsrGenerator : IZatcaCsrGenerator
{
    private static readonly DerObjectIdentifier CertificateTemplateNameOid = new("1.3.6.1.4.1.311.20.2");

    public CsrGenerationResult GenerateCsr(CsrGenerationRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);

        if (string.IsNullOrWhiteSpace(request.CommonName))
        {
            throw new ArgumentException("CommonName cannot be null or whitespace.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationUnit))
        {
            throw new ArgumentException("OrganizationUnit cannot be null or whitespace.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.OrganizationName))
        {
            throw new ArgumentException("OrganizationName cannot be null or whitespace.", nameof(request));
        }

        if (string.IsNullOrWhiteSpace(request.CountryCode))
        {
            throw new ArgumentException("CountryCode cannot be null or whitespace.", nameof(request));
        }

        // 1. Generate ECDSA secp256k1 keypair
        var ecParams = SecNamedCurves.GetByName("secp256k1");
        var domainParams = new ECDomainParameters(ecParams.Curve, ecParams.G, ecParams.N, ecParams.H);
        var keyGen = new ECKeyPairGenerator();
        keyGen.Init(new ECKeyGenerationParameters(domainParams, new SecureRandom()));
        var keyPair = keyGen.GenerateKeyPair();

        // 2. Build standard X.509 Subject DN
        var attrs = new Dictionary<DerObjectIdentifier, string>
        {
            [X509Name.C] = request.CountryCode.Trim(),
            [X509Name.O] = request.OrganizationName.Trim(),
            [X509Name.OU] = request.OrganizationUnit.Trim(),
            [X509Name.CN] = request.CommonName.Trim()
        };
        var order = new List<DerObjectIdentifier> { X509Name.C, X509Name.O, X509Name.OU, X509Name.CN };
        var subject = new X509Name(order, attrs);

        // 3. Add custom Microsoft extension OID 1.3.6.1.4.1.311.20.2 (CertificateTemplateName)
        var templateName = request.Environment == CsidEnvironment.Production
            ? "ZATCA-Code-Signing"
            : "PREZATCA-Code-Signing";

        var extGen = new X509ExtensionsGenerator();
        extGen.AddExtension(
            CertificateTemplateNameOid,
            critical: false,
            new DerUtf8String(templateName)
        );
        var extensions = extGen.Generate();
        var attr = new AttributePkcs(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest, new DerSet(extensions));
        var attributes = new DerSet(attr);

        // 4. Construct and sign PKCS#10 Certificate Request
        var pkcs10 = new Pkcs10CertificationRequest("SHA256WITHECDSA", subject, keyPair.Public, attributes, keyPair.Private);

        // 5. Export keys and CSR to standard PEM format
        var privateKeyPem = ExportPrivateKeyToPem(keyPair.Private);
        var publicKeyPem = ExportPublicKeyToPem(keyPair.Public);
        var csrPem = ExportCsrToPem(pkcs10);

        return new CsrGenerationResult(privateKeyPem, publicKeyPem, csrPem, request.Environment);
    }

    private static string ExportPrivateKeyToPem(AsymmetricKeyParameter privateKey)
    {
        using var stringWriter = new StringWriter();
        var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(stringWriter);
        var pkcs8Gen = new Pkcs8Generator(privateKey);
        pemWriter.WriteObject(pkcs8Gen);
        pemWriter.Writer.Flush();
        return stringWriter.ToString();
    }

    private static string ExportPublicKeyToPem(AsymmetricKeyParameter publicKey)
    {
        using var stringWriter = new StringWriter();
        var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(stringWriter);
        pemWriter.WriteObject(publicKey);
        pemWriter.Writer.Flush();
        return stringWriter.ToString();
    }

    private static string ExportCsrToPem(Pkcs10CertificationRequest pkcs10)
    {
        using var stringWriter = new StringWriter();
        var pemWriter = new Org.BouncyCastle.OpenSsl.PemWriter(stringWriter);
        pemWriter.WriteObject(pkcs10);
        pemWriter.Writer.Flush();
        return stringWriter.ToString();
    }
}

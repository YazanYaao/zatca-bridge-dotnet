using System;
using System.IO;
using Org.BouncyCastle.Asn1;
using Org.BouncyCastle.Asn1.Pkcs;
using Org.BouncyCastle.Asn1.X509;
using Org.BouncyCastle.OpenSsl;
using Org.BouncyCastle.Pkcs;
using Xunit;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Cryptography;

namespace ZatcaBridge.UnitTests.Infrastructure;

public class ZatcaCsrGeneratorTests
{
    private readonly IZatcaCsrGenerator _generator = new ZatcaCsrGenerator();

    [Fact]
    public void GenerateCsr_SimulationEnvironment_GeneratesValidSecp256k1KeysAndPrezatcaCsr()
    {
        // Arrange
        var request = new CsrGenerationRequest(
            CommonName: "TST-883313294-310123456700003",
            OrganizationUnit: "Riyadh Branch",
            OrganizationName: "Acme Saudi Co",
            CountryCode: "SA",
            Environment: CsidEnvironment.Simulation
        );

        // Act
        var result = _generator.GenerateCsr(request);

        // Assert: Keys and PEM structure
        Assert.NotNull(result);
        Assert.Equal(CsidEnvironment.Simulation, result.Environment);
        Assert.Contains("-----BEGIN", result.PrivateKeyPem);
        Assert.Contains("PRIVATE KEY-----", result.PrivateKeyPem);
        Assert.Contains("-----BEGIN CERTIFICATE REQUEST-----", result.CsrPem);
        Assert.Contains("-----END CERTIFICATE REQUEST-----", result.CsrPem);

        // Assert: Parse CSR with BouncyCastle to verify ASN.1 structure and signature
        using var reader = new StringReader(result.CsrPem);
        var pemReader = new PemReader(reader);
        var obj = pemReader.ReadObject();
        var pkcs10 = Assert.IsAssignableFrom<Pkcs10CertificationRequest>(obj);

        // Verify cryptographic signature integrity of the CSR itself
        Assert.True(pkcs10.Verify(), "CSR self-signature verification failed.");

        // Verify Subject DN
        var subject = pkcs10.GetCertificationRequestInfo().Subject;
        Assert.Contains("CN=TST-883313294-310123456700003", subject.ToString());
        Assert.Contains("OU=Riyadh Branch", subject.ToString());
        Assert.Contains("O=Acme Saudi Co", subject.ToString());
        Assert.Contains("C=SA", subject.ToString());

        // Verify CertificateTemplateName OID 1.3.6.1.4.1.311.20.2
        var attributes = pkcs10.GetCertificationRequestInfo().Attributes;
        var foundTemplate = false;
        var templateOid = new DerObjectIdentifier("1.3.6.1.4.1.311.20.2");

        foreach (var attrObj in attributes)
        {
            var attr = AttributePkcs.GetInstance(attrObj);
            if (attr != null && attr.AttrType.Equals(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest))
            {
                var extensions = X509Extensions.GetInstance(attr.AttrValues[0]);
                var extension = extensions.GetExtension(templateOid);
                if (extension != null)
                {
                    var val = Asn1Object.FromByteArray(extension.Value.GetOctets());
                    Assert.Contains("PREZATCA-Code-Signing", val.ToString());
                    foundTemplate = true;
                }
            }
        }

        Assert.True(foundTemplate, "Custom OID 1.3.6.1.4.1.311.20.2 with PREZATCA-Code-Signing was not found in CSR extensions.");
    }

    [Fact]
    public void GenerateCsr_ProductionEnvironment_EmbedsZatcaCodeSigningTemplate()
    {
        // Arrange
        var request = new CsrGenerationRequest(
            CommonName: "PRD-310123456700003-Solution",
            OrganizationUnit: "Finance Head Office",
            OrganizationName: "Saudi Enterprise Ltd",
            CountryCode: "SA",
            Environment: CsidEnvironment.Production
        );

        // Act
        var result = _generator.GenerateCsr(request);

        // Assert: Parse CSR and verify ZATCA-Code-Signing extension
        using var reader = new StringReader(result.CsrPem);
        var pemReader = new PemReader(reader);
        var pkcs10 = (Pkcs10CertificationRequest)pemReader.ReadObject();

        Assert.True(pkcs10.Verify());

        var templateOid = new DerObjectIdentifier("1.3.6.1.4.1.311.20.2");
        var foundTemplate = false;
        foreach (var attrObj in pkcs10.GetCertificationRequestInfo().Attributes)
        {
            var attr = AttributePkcs.GetInstance(attrObj);
            if (attr != null && attr.AttrType.Equals(PkcsObjectIdentifiers.Pkcs9AtExtensionRequest))
            {
                var extensions = X509Extensions.GetInstance(attr.AttrValues[0]);
                var extension = extensions.GetExtension(templateOid);
                if (extension != null)
                {
                    var val = Asn1Object.FromByteArray(extension.Value.GetOctets());
                    Assert.Contains("ZATCA-Code-Signing", val.ToString());
                    Assert.DoesNotContain("PREZATCA", val.ToString());
                    foundTemplate = true;
                }
            }
        }

        Assert.True(foundTemplate, "Custom OID 1.3.6.1.4.1.311.20.2 with ZATCA-Code-Signing was not found in CSR extensions.");
    }

    [Theory]
    [InlineData("", "OU", "O", "SA")]
    [InlineData("CN", "", "O", "SA")]
    [InlineData("CN", "OU", "", "SA")]
    [InlineData("CN", "OU", "O", "")]
    public void GenerateCsr_InvalidParameters_ThrowsArgumentException(string cn, string ou, string o, string c)
    {
        var request = new CsrGenerationRequest(cn, ou, o, c, CsidEnvironment.Simulation);
        Assert.ThrowsAny<ArgumentException>(() => _generator.GenerateCsr(request));
    }

    [Fact]
    public void CsidCredential_CreationAndActivation_SetsPropertiesCorrectly()
    {
        var id = Guid.NewGuid();
        var credential = new CsidCredential(
            id: id,
            privateKeyPem: "-----BEGIN PRIVATE KEY-----\nMIG...\n-----END PRIVATE KEY-----",
            publicKeyPem: "-----BEGIN PUBLIC KEY-----\nMFk...\n-----END PUBLIC KEY-----",
            csrPem: "-----BEGIN CERTIFICATE REQUEST-----\nMII...\n-----END CERTIFICATE REQUEST-----",
            certificatePem: "-----BEGIN CERTIFICATE-----\nMII...\n-----END CERTIFICATE-----",
            secret: "secret-12345",
            requestId: "req-98765",
            environment: CsidEnvironment.Simulation,
            isActive: true,
            createdAt: DateTimeOffset.UtcNow
        );

        Assert.Equal(id, credential.Id);
        Assert.True(credential.IsActive);
        Assert.Equal(CsidEnvironment.Simulation, credential.Environment);
        Assert.Equal("secret-12345", credential.Secret);
        Assert.Equal("req-98765", credential.RequestId);
    }
}

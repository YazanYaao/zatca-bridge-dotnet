using ZatcaBridge.Domain.Cryptography;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Parameters for generating a ZATCA-compliant PKCS#10 Certificate Signing Request (CSR).
/// </summary>
public sealed record CsrGenerationRequest(
    string CommonName,
    string OrganizationUnit,
    string OrganizationName,
    string CountryCode = "SA",
    CsidEnvironment Environment = CsidEnvironment.Simulation,
    string? SerialNumber = null,
    string? OrganizationIdentifier = null,
    string? InvoiceType = null,
    string? Location = null,
    string? Industry = null
);

/// <summary>
/// Result containing the generated secp256k1 keypair and PKCS#10 CSR in PEM format.
/// </summary>
public sealed record CsrGenerationResult(
    string PrivateKeyPem,
    string PublicKeyPem,
    string CsrPem,
    CsidEnvironment Environment
);

/// <summary>
/// Port for generating ECDSA secp256k1 keypairs and ZATCA Phase 2 PKCS#10 Certificate Signing Requests.
/// </summary>
public interface IZatcaCsrGenerator
{
    CsrGenerationResult GenerateCsr(CsrGenerationRequest request);
}

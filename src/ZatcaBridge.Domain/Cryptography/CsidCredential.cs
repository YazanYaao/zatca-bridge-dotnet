using System;

namespace ZatcaBridge.Domain.Cryptography;

/// <summary>
/// Domain model representing a cryptographic CSID credential set for ZATCA Phase 2 signing and authentication.
/// </summary>
public sealed record CsidCredential
{
    public Guid Id { get; init; }
    public string PrivateKeyPem { get; init; }
    public string? PublicKeyPem { get; init; }
    public string? CsrPem { get; init; }
    public string? CertificatePem { get; init; }
    public string? Secret { get; init; }
    public string? RequestId { get; init; }
    public CsidEnvironment Environment { get; init; }
    public bool IsActive { get; init; }
    public DateTimeOffset CreatedAt { get; init; }

    public CsidCredential(
        Guid id,
        string privateKeyPem,
        string? publicKeyPem = null,
        string? csrPem = null,
        string? certificatePem = null,
        string? secret = null,
        string? requestId = null,
        CsidEnvironment environment = CsidEnvironment.Simulation,
        bool isActive = false,
        DateTimeOffset? createdAt = null)
    {
        if (id == Guid.Empty)
        {
            throw new ArgumentException("Id cannot be empty.", nameof(id));
        }

        if (string.IsNullOrWhiteSpace(privateKeyPem))
        {
            throw new ArgumentException("Private key PEM cannot be null or whitespace.", nameof(privateKeyPem));
        }

        Id = id;
        PrivateKeyPem = privateKeyPem.Trim();
        PublicKeyPem = publicKeyPem?.Trim();
        CsrPem = csrPem?.Trim();
        CertificatePem = certificatePem?.Trim();
        Secret = secret?.Trim();
        RequestId = requestId?.Trim();
        Environment = environment;
        IsActive = isActive;
        CreatedAt = createdAt ?? DateTimeOffset.UtcNow;
    }
}

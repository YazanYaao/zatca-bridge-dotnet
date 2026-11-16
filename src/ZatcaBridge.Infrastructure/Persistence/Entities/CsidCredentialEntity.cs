using System;

namespace ZatcaBridge.Infrastructure.Persistence.Entities;

/// <summary>
/// EF Core database entity for storing ZATCA Phase 2 CSID cryptographic credentials.
/// </summary>
public class CsidCredentialEntity
{
    public Guid Id { get; set; }
    public string PrivateKeyPem { get; set; } = string.Empty;
    public string? PublicKeyPem { get; set; }
    public string? CsrPem { get; set; }
    public string? CertificatePem { get; set; }
    public string? Secret { get; set; }
    public string? RequestId { get; set; }
    public int Environment { get; set; }
    public bool IsActive { get; set; }
    public DateTimeOffset CreatedAt { get; set; }
}

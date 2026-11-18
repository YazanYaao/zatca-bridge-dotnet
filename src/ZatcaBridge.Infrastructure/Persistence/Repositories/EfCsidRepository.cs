using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Microsoft.EntityFrameworkCore;
using ZatcaBridge.Application.Common.Interfaces;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Infrastructure.Persistence.Entities;

namespace ZatcaBridge.Infrastructure.Persistence.Repositories;

/// <summary>
/// Entity Framework Core implementation of <see cref="ICsidRepository"/> for storing and retrieving CSID credentials.
/// </summary>
public class EfCsidRepository : ICsidRepository
{
    private readonly ZatcaDbContext _context;
    private readonly ICsidSecretProtector? _secretProtector;

    public EfCsidRepository(ZatcaDbContext context, ICsidSecretProtector? secretProtector = null)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
        _secretProtector = secretProtector;
    }

    /// <inheritdoc />
    public async Task<CsidCredential?> GetActiveAsync(CancellationToken cancellationToken = default)
    {
        var entity = await _context.CsidCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.IsActive, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task<CsidCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var entity = await _context.CsidCredentials
            .AsNoTracking()
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        return entity is null ? null : MapToDomain(entity);
    }

    /// <inheritdoc />
    public async Task SaveAsync(CsidCredential credential, CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(credential);

        if (credential.IsActive)
        {
            var currentlyActive = await _context.CsidCredentials
                .Where(c => c.IsActive && c.Id != credential.Id)
                .ToListAsync(cancellationToken);

            foreach (var active in currentlyActive)
            {
                active.IsActive = false;
            }
        }

        var existing = await _context.CsidCredentials
            .FirstOrDefaultAsync(c => c.Id == credential.Id, cancellationToken);

        if (existing is null)
        {
            _context.CsidCredentials.Add(MapToEntity(credential));
        }
        else
        {
            existing.PrivateKeyPem = (_secretProtector != null ? _secretProtector.Protect(credential.PrivateKeyPem) : credential.PrivateKeyPem) ?? string.Empty;
            existing.PublicKeyPem = credential.PublicKeyPem;
            existing.CsrPem = credential.CsrPem;
            existing.CertificatePem = credential.CertificatePem;
            existing.Secret = _secretProtector != null ? _secretProtector.Protect(credential.Secret) : credential.Secret;
            existing.RequestId = credential.RequestId;
            existing.Environment = (int)credential.Environment;
            existing.IsActive = credential.IsActive;
            existing.CreatedAt = credential.CreatedAt;
        }

        await _context.SaveChangesAsync(cancellationToken);
    }

    /// <inheritdoc />
    public async Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default)
    {
        var target = await _context.CsidCredentials
            .FirstOrDefaultAsync(c => c.Id == id, cancellationToken);

        if (target is null)
        {
            throw new InvalidOperationException($"CSID credential with ID '{id}' was not found.");
        }

        var currentlyActive = await _context.CsidCredentials
            .Where(c => c.IsActive && c.Id != id)
            .ToListAsync(cancellationToken);

        foreach (var active in currentlyActive)
            active.IsActive = false;

        target.IsActive = true;
        await _context.SaveChangesAsync(cancellationToken);
    }

    private CsidCredential MapToDomain(CsidCredentialEntity e)
    {
        var privateKeyPem = (_secretProtector != null ? _secretProtector.Unprotect(e.PrivateKeyPem) : e.PrivateKeyPem) ?? string.Empty;
        var secret = _secretProtector != null ? _secretProtector.Unprotect(e.Secret) : e.Secret;

        return new(
            id: e.Id,
            privateKeyPem: privateKeyPem,
            publicKeyPem: e.PublicKeyPem,
            csrPem: e.CsrPem,
            certificatePem: e.CertificatePem,
            secret: secret,
            requestId: e.RequestId,
            environment: (CsidEnvironment)e.Environment,
            isActive: e.IsActive,
            createdAt: e.CreatedAt
        );
    }

    private CsidCredentialEntity MapToEntity(CsidCredential d)
    {
        var privateKeyPem = (_secretProtector != null ? _secretProtector.Protect(d.PrivateKeyPem) : d.PrivateKeyPem) ?? string.Empty;
        var secret = _secretProtector != null ? _secretProtector.Protect(d.Secret) : d.Secret;

        return new()
        {
            Id = d.Id,
            PrivateKeyPem = privateKeyPem,
            PublicKeyPem = d.PublicKeyPem,
            CsrPem = d.CsrPem,
            CertificatePem = d.CertificatePem,
            Secret = secret,
            RequestId = d.RequestId,
            Environment = (int)d.Environment,
            IsActive = d.IsActive,
            CreatedAt = d.CreatedAt
        };
    }
}

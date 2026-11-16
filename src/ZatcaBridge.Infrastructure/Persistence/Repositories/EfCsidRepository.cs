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

    public EfCsidRepository(ZatcaDbContext context)
    {
        _context = context ?? throw new ArgumentNullException(nameof(context));
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
            existing.PrivateKeyPem = credential.PrivateKeyPem;
            existing.PublicKeyPem = credential.PublicKeyPem;
            existing.CsrPem = credential.CsrPem;
            existing.CertificatePem = credential.CertificatePem;
            existing.Secret = credential.Secret;
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

    private static CsidCredential MapToDomain(CsidCredentialEntity e) =>
        new(
            id: e.Id,
            privateKeyPem: e.PrivateKeyPem,
            publicKeyPem: e.PublicKeyPem,
            csrPem: e.CsrPem,
            certificatePem: e.CertificatePem,
            secret: e.Secret,
            requestId: e.RequestId,
            environment: (CsidEnvironment)e.Environment,
            isActive: e.IsActive,
            createdAt: e.CreatedAt
        );

    private static CsidCredentialEntity MapToEntity(CsidCredential d) =>
        new()
        {
            Id = d.Id,
            PrivateKeyPem = d.PrivateKeyPem,
            PublicKeyPem = d.PublicKeyPem,
            CsrPem = d.CsrPem,
            CertificatePem = d.CertificatePem,
            Secret = d.Secret,
            RequestId = d.RequestId,
            Environment = (int)d.Environment,
            IsActive = d.IsActive,
            CreatedAt = d.CreatedAt
        };
}

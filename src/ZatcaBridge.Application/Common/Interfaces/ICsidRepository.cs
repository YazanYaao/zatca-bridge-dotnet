using System;
using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Domain.Cryptography;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Port for persisting and retrieving CSID cryptographic credentials.
/// </summary>
public interface ICsidRepository
{
    /// <summary>
    /// Retrieves the currently active CSID credential, or null if none is onboarded.
    /// </summary>
    Task<CsidCredential?> GetActiveAsync(CancellationToken cancellationToken = default);

    /// <summary>
    /// Retrieves a CSID credential by its unique identifier.
    /// </summary>
    Task<CsidCredential?> GetByIdAsync(Guid id, CancellationToken cancellationToken = default);

    /// <summary>
    /// Persists a new or updated CSID credential.
    /// If credential.IsActive is true, all other active credentials are automatically marked inactive.
    /// </summary>
    Task SaveAsync(CsidCredential credential, CancellationToken cancellationToken = default);

    /// <summary>
    /// Activates the credential with the specified id and deactivates any other active credentials.
    /// </summary>
    Task SetActiveAsync(Guid id, CancellationToken cancellationToken = default);
}

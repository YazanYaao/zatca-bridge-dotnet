using System.Threading;
using System.Threading.Tasks;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Port for retrieving and tracking cryptographic hash chain states.
/// </summary>
public interface IHashChainRepository
{
    /// <summary>
    /// Retrieves the most recent hash chain record, or null if no invoices have been issued yet.
    /// </summary>
    Task<HashChainRecord?> GetLatestRecordAsync(CancellationToken cancellationToken = default);
}

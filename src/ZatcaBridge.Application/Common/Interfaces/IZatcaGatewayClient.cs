using System.Threading;
using System.Threading.Tasks;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Response payload returned by ZATCA gateway endpoints.
/// </summary>
public sealed record ZatcaGatewayResponse(
    bool IsSuccess,
    bool IsRejected,
    string? ClearanceStatus = null,
    string? ReportingStatus = null,
    string? ClearedInvoiceXml = null,
    string? ErrorMessage = null,
    string[]? ValidationErrors = null
);

/// <summary>
/// Gateway client contract for dispatching invoices to ZATCA Phase 2 clearance and reporting endpoints.
/// </summary>
public interface IZatcaGatewayClient
{
    /// <summary>
    /// Synchronously dispatches a standard B2B tax invoice to the ZATCA Clearance API.
    /// </summary>
    Task<ZatcaGatewayResponse> ClearInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default);

    /// <summary>
    /// Asynchronously dispatches a simplified B2C tax invoice to the ZATCA Reporting API.
    /// </summary>
    Task<ZatcaGatewayResponse> ReportInvoiceAsync(string invoiceHash, string ublXml, CancellationToken cancellationToken = default);
}

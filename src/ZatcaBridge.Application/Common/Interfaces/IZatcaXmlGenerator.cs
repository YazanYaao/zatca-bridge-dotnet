namespace ZatcaBridge.Application.Common.Interfaces;

using ZatcaBridge.Domain.Entities;

/// <summary>
/// Service contract for generating ZATCA Phase 2 compliant UBL 2.1 XML invoices.
/// </summary>
public interface IZatcaXmlGenerator
{
    /// <summary>
    /// Generates standard ZATCA Phase 2 UBL 2.1 XML for an invoice.
    /// </summary>
    string GenerateXml(Invoice invoice, string? qrCodeBase64 = null);

    /// <summary>
    /// Generates standard ZATCA Phase 2 UBL 2.1 XML bytes (UTF-8 without BOM).
    /// </summary>
    byte[] GenerateXmlBytes(Invoice invoice, string? qrCodeBase64 = null);
}

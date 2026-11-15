namespace ZatcaBridge.Domain.Enums;

/// <summary>
/// Transaction classification for ZATCA E-Invoicing.
/// </summary>
public enum InvoiceTransactionType
{
    /// <summary>
    /// Standard Tax Invoice (B2B / B2G) requiring real-time ZATCA clearance before issuance.
    /// </summary>
    B2B,

    /// <summary>
    /// Simplified Tax Invoice (B2C) issued immediately with QR code and reported within 24h.
    /// </summary>
    B2C
}

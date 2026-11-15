namespace ZatcaBridge.Domain.Enums;

/// <summary>
/// ZATCA Tax Category Codes according to UN/ECE 5305 and ZATCA Phase 2 E-Invoicing standards.
/// </summary>
public enum TaxCategoryCode
{
    /// <summary>
    /// Standard rate (15% in KSA)
    /// </summary>
    Standard,

    /// <summary>
    /// Zero-rated goods or services (0%)
    /// </summary>
    ZeroRated,

    /// <summary>
    /// Exempt from tax (0%)
    /// </summary>
    Exempt,

    /// <summary>
    /// Out of scope / Not subject to VAT (0%)
    /// </summary>
    OutOfScope
}

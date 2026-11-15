namespace ZatcaBridge.Domain.Enums;

/// <summary>
/// ZATCA Invoice Type Codes according to UN/ECE 1001.
/// </summary>
public enum InvoiceTypeCode
{
    /// <summary>
    /// Standard Tax Invoice (Code 388)
    /// </summary>
    TaxInvoice = 388,

    /// <summary>
    /// Simplified Tax Invoice (Code 380 / 388 subtype)
    /// </summary>
    SimplifiedTaxInvoice = 380,

    /// <summary>
    /// Debit Note (Code 383)
    /// </summary>
    DebitNote = 383,

    /// <summary>
    /// Credit Note (Code 381)
    /// </summary>
    CreditNote = 381,

    /// <summary>
    /// Prepayment Invoice (Code 386)
    /// </summary>
    PrepaymentInvoice = 386
}

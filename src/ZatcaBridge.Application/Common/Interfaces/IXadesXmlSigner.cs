using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;

namespace ZatcaBridge.Application.Common.Interfaces;

/// <summary>
/// Result of the XAdES-BES digital signing and Phase 2 QR generation pipeline.
/// </summary>
public sealed record XadesSigningResult(
    string SignedXml,
    string InvoiceHashBase64,
    string DigitalSignatureBase64,
    string QrCodeBase64
);

/// <summary>
/// Port for applying canonicalization, XAdES-BES digital signatures, and Phase 2 QR TLV tags 1–9 to UBL 2.1 invoices.
/// </summary>
public interface IXadesXmlSigner
{
    /// <summary>
    /// Executes the two-pass ZATCA Phase 2 signing sequence: computes the canonical invoice hash,
    /// signs with the secp256k1 private key, encodes Tags 1–9 in the QR code, and embeds the XAdES enveloped signature.
    /// </summary>
    XadesSigningResult SignInvoice(Invoice invoice, CsidCredential credential);
}

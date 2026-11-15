using System;
using System.Collections.Generic;
using System.Text.RegularExpressions;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;

namespace ZatcaBridge.Domain.ValueObjects;

/// <summary>
/// Value object holding all data fields required for ZATCA Phase 1 & 2 QR code generation.
/// </summary>
public sealed record ZatcaQrCodeData
{
    private static readonly Regex KsaVatRegex = new(@"^3\d{13}3$", RegexOptions.Compiled);

    public string SellerName { get; }
    public string VatRegistrationNumber { get; }
    public DateTimeOffset Timestamp { get; }
    public decimal InvoiceTotal { get; }
    public decimal VatTotal { get; }

    // Phase 2 Cryptographic fields (Tags 6 to 9)
    public byte[]? InvoiceHash { get; }
    public byte[]? DigitalSignature { get; }
    public byte[]? PublicKey { get; }
    public byte[]? CertificateSignature { get; }

    public ZatcaQrCodeData(
        string sellerName,
        string vatRegistrationNumber,
        DateTimeOffset timestamp,
        decimal invoiceTotal,
        decimal vatTotal,
        byte[]? invoiceHash = null,
        byte[]? digitalSignature = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
    {
        if (string.IsNullOrWhiteSpace(sellerName))
        {
            throw new ArgumentException("Seller name cannot be null or whitespace.", nameof(sellerName));
        }

        if (string.IsNullOrWhiteSpace(vatRegistrationNumber))
        {
            throw new ArgumentException("VAT registration number cannot be null or whitespace.", nameof(vatRegistrationNumber));
        }

        var trimmedVat = vatRegistrationNumber.Trim();
        if (trimmedVat.Length != 15 || !KsaVatRegex.IsMatch(trimmedVat))
        {
            throw new ArgumentException($"Invalid KSA VAT Registration Number: '{vatRegistrationNumber}'. Must be 15 digits starting and ending with 3.", nameof(vatRegistrationNumber));
        }

        if (invoiceTotal < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(invoiceTotal), "Invoice total cannot be negative.");
        }

        if (vatTotal < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(vatTotal), "VAT total cannot be negative.");
        }

        SellerName = sellerName.Trim();
        VatRegistrationNumber = trimmedVat;
        Timestamp = timestamp;
        InvoiceTotal = invoiceTotal;
        VatTotal = vatTotal;

        InvoiceHash = invoiceHash is null ? null : (byte[])invoiceHash.Clone();
        DigitalSignature = digitalSignature is null ? null : (byte[])digitalSignature.Clone();
        PublicKey = publicKey is null ? null : (byte[])publicKey.Clone();
        CertificateSignature = certificateSignature is null ? null : (byte[])certificateSignature.Clone();
    }

    public static ZatcaQrCodeData FromInvoice(
        Invoice invoice,
        byte[]? invoiceHash = null,
        byte[]? digitalSignature = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        var dateTime = invoice.IssueDate.ToDateTime(invoice.IssueTime);
        var timestamp = new DateTimeOffset(dateTime, TimeSpan.FromHours(3)); // KSA standard time (+03:00)

        return new ZatcaQrCodeData(
            invoice.Seller.RegistrationName,
            invoice.Seller.VatNumber ?? throw new InvalidOperationException("Seller VAT number is required."),
            timestamp,
            invoice.MonetaryTotal.PayableAmount,
            invoice.TotalTaxAmount,
            invoiceHash,
            digitalSignature,
            publicKey,
            certificateSignature
        );
    }

    /// <summary>
    /// Converts the QR code fields into ordered ZATCA TLV tags.
    /// </summary>
    public IEnumerable<TlvTag> ToTags()
    {
        // Tag 1: Seller's Name
        yield return TlvTag.FromUtf8(1, SellerName);

        // Tag 2: VAT Registration Number
        yield return TlvTag.FromUtf8(2, VatRegistrationNumber);

        // Tag 3: Time Stamp
        yield return TlvTag.FromDateTimeOffset(3, Timestamp);

        // Tag 4: Invoice Total (with VAT)
        yield return TlvTag.FromDecimal(4, InvoiceTotal);

        // Tag 5: Total VAT
        yield return TlvTag.FromDecimal(5, VatTotal);

        // Tag 6: XML Invoice Hash
        if (InvoiceHash is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(6, InvoiceHash);
        }

        // Tag 7: ECDSA Digital Signature
        if (DigitalSignature is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(7, DigitalSignature);
        }

        // Tag 8: ECDSA Public Key / Certificate
        if (PublicKey is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(8, PublicKey);
        }

        // Tag 9: Certificate Signature (Stamp)
        if (CertificateSignature is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(9, CertificateSignature);
        }
    }
}

using System;
using System.Collections.Generic;
using System.Globalization;
using System.Text.RegularExpressions;
using ZatcaBridge.Domain.Cryptography;
using ZatcaBridge.Domain.Entities;

namespace ZatcaBridge.Domain.ValueObjects;

/// <summary>
/// Value object holding all data fields required for ZATCA Phase 1 &amp; 2 QR code generation.
/// </summary>
public sealed record ZatcaQrCodeData
{
    private static readonly Regex KsaVatRegex = new(@"^3\d{13}3$", RegexOptions.Compiled);

    public string SellerName { get; }
    public string VatRegistrationNumber { get; }

    /// <summary>
    /// Invoice timestamp as recorded in the UBL XML (IssueDate + IssueTime, no timezone).
    /// Format for QR Tag 3: YYYY-MM-DDTHH:mm:ss (no Z suffix, per ZATCA SDK).
    /// </summary>
    public string TimestampString { get; }

    public DateTimeOffset Timestamp => DateTimeOffset.Parse(TimestampString, CultureInfo.InvariantCulture);

    public decimal InvoiceTotal { get; }
    public decimal VatTotal { get; }

    // Phase 2 Cryptographic fields (Tags 6 to 9)
    // Tag 6 & 7: Base64 strings stored as UTF-8 bytes (ZATCA SDK requirement)
    // Tag 8 & 9: Raw DER bytes
    public string? InvoiceHashBase64 { get; }
    public byte[]? InvoiceHash => string.IsNullOrEmpty(InvoiceHashBase64) ? null : Convert.FromBase64String(InvoiceHashBase64);

    public string? DigitalSignatureBase64 { get; }
    public byte[]? DigitalSignature => string.IsNullOrEmpty(DigitalSignatureBase64) ? null : Convert.FromBase64String(DigitalSignatureBase64);

    public byte[]? PublicKey { get; }
    public byte[]? CertificateSignature { get; }

    public ZatcaQrCodeData(
        string sellerName,
        string vatRegistrationNumber,
        string timestampString,
        decimal invoiceTotal,
        decimal vatTotal,
        string? invoiceHashBase64 = null,
        string? digitalSignatureBase64 = null,
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
        TimestampString = timestampString ?? throw new ArgumentNullException(nameof(timestampString));
        InvoiceTotal = invoiceTotal;
        VatTotal = vatTotal;

        InvoiceHashBase64 = invoiceHashBase64;
        DigitalSignatureBase64 = digitalSignatureBase64;
        PublicKey = publicKey is null ? null : (byte[])publicKey.Clone();
        CertificateSignature = certificateSignature is null ? null : (byte[])certificateSignature.Clone();
    }

    public ZatcaQrCodeData(
        string sellerName,
        string vatRegistrationNumber,
        string timestampString,
        decimal invoiceTotal,
        decimal vatTotal,
        byte[]? invoiceHash,
        byte[]? digitalSignature = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
        : this(
            sellerName,
            vatRegistrationNumber,
            timestampString,
            invoiceTotal,
            vatTotal,
            invoiceHash is null ? null : Convert.ToBase64String(invoiceHash),
            digitalSignature is null ? null : Convert.ToBase64String(digitalSignature),
            publicKey,
            certificateSignature)
    {
    }

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
        : this(
            sellerName,
            vatRegistrationNumber,
            timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            invoiceTotal,
            vatTotal,
            invoiceHash,
            digitalSignature,
            publicKey,
            certificateSignature)
    {
    }

    public ZatcaQrCodeData(
        string sellerName,
        string vatRegistrationNumber,
        DateTimeOffset timestamp,
        decimal invoiceTotal,
        decimal vatTotal,
        string? invoiceHashBase64,
        string? digitalSignatureBase64 = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
        : this(
            sellerName,
            vatRegistrationNumber,
            timestamp.ToString("yyyy-MM-ddTHH:mm:ss", CultureInfo.InvariantCulture),
            invoiceTotal,
            vatTotal,
            invoiceHashBase64,
            digitalSignatureBase64,
            publicKey,
            certificateSignature)
    {
    }

    public static ZatcaQrCodeData FromInvoice(
        Invoice invoice,
        string? invoiceHashBase64 = null,
        string? digitalSignatureBase64 = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
    {
        ArgumentNullException.ThrowIfNull(invoice);

        // ZATCA REQUIREMENT: Tag 3 uses exact IssueDate+IssueTime without timezone conversion
        var timestampString = $"{invoice.IssueDate:yyyy-MM-dd}T{invoice.IssueTime:HH:mm:ss}";

        return new ZatcaQrCodeData(
            invoice.Seller.RegistrationName,
            invoice.Seller.VatNumber ?? throw new InvalidOperationException("Seller VAT number is required."),
            timestampString,
            invoice.MonetaryTotal.PayableAmount,
            invoice.TotalTaxAmount,
            invoiceHashBase64,
            digitalSignatureBase64,
            publicKey,
            certificateSignature
        );
    }

    public static ZatcaQrCodeData FromInvoice(
        Invoice invoice,
        byte[]? invoiceHash,
        byte[]? digitalSignature = null,
        byte[]? publicKey = null,
        byte[]? certificateSignature = null)
    {
        return FromInvoice(
            invoice,
            invoiceHash is null ? null : Convert.ToBase64String(invoiceHash),
            digitalSignature is null ? null : Convert.ToBase64String(digitalSignature),
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

        // Tag 3: Time Stamp — YYYY-MM-DDTHH:mm:ss (no Z, matches UBL XML IssueDate+IssueTime)
        yield return TlvTag.FromUtf8(3, TimestampString);

        // Tag 4: Invoice Total (with VAT)
        yield return TlvTag.FromDecimal(4, InvoiceTotal);

        // Tag 5: Total VAT
        yield return TlvTag.FromDecimal(5, VatTotal);

        // Tag 6: XML Invoice Hash — UTF-8 bytes of the Base64 string (44 chars for SHA-256)
        if (!string.IsNullOrEmpty(InvoiceHashBase64))
        {
            yield return TlvTag.FromUtf8(6, InvoiceHashBase64);
        }

        // Tag 7: ECDSA Digital Signature — UTF-8 bytes of the Base64 DER string
        if (!string.IsNullOrEmpty(DigitalSignatureBase64))
        {
            yield return TlvTag.FromUtf8(7, DigitalSignatureBase64);
        }

        // Tag 8: ECDSA Public Key (SubjectPublicKeyInfo DER) — raw bytes
        if (PublicKey is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(8, PublicKey);
        }

        // Tag 9: Certificate Signature (Stamp) — raw bytes
        if (CertificateSignature is { Length: > 0 })
        {
            yield return TlvTag.FromBytes(9, CertificateSignature);
        }
    }
}

using System;
using System.Security.Cryptography.X509Certificates;
using System.Text.RegularExpressions;

namespace ZatcaBridge.Application.Common.Security;

/// <summary>
/// Helper to extract VAT number and Organization Name from ZATCA X.509 certificates.
/// </summary>
public static class CertificateMetadataExtractor
{
    private static readonly Regex VatRegex = new(@"\b(3\d{13}3)\b", RegexOptions.Compiled);
    private static readonly Regex OrgRegex = new(@"\bO=([^,]+)", RegexOptions.Compiled);

    /// <summary>
    /// Extracts the 15-digit taxpayer VAT registration number from the certificate's Subject.
    /// </summary>
    public static string? ExtractVatNumber(string? certPem)
    {
        if (string.IsNullOrWhiteSpace(certPem)) return null;

        try
        {
            var clean = certPem
                .Replace("-----BEGIN CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
                .Replace("-----END CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            var bytes = Convert.FromBase64String(clean);
            using var cert = X509CertificateLoader.LoadCertificate(bytes);
            var match = VatRegex.Match(cert.Subject);
            return match.Success ? match.Value : null;
        }
        catch
        {
            return null;
        }
    }

    /// <summary>
    /// Extracts the Organization Name (O=...) from the certificate's Subject.
    /// </summary>
    public static string? ExtractOrganizationName(string? certPem)
    {
        if (string.IsNullOrWhiteSpace(certPem)) return null;

        try
        {
            var clean = certPem
                .Replace("-----BEGIN CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
                .Replace("-----END CERTIFICATE-----", "", StringComparison.OrdinalIgnoreCase)
                .Replace("\r", "")
                .Replace("\n", "")
                .Trim();

            var bytes = Convert.FromBase64String(clean);
            using var cert = X509CertificateLoader.LoadCertificate(bytes);
            var match = OrgRegex.Match(cert.Subject);
            return match.Success ? match.Groups[1].Value.Trim() : null;
        }
        catch
        {
            return null;
        }
    }
}

using System;
using System.Security.Cryptography;
using System.Text;

namespace ZatcaBridge.Domain.Cryptography;

public sealed record InvoiceHashResult(byte[] Digest, string Base64Hash);

/// <summary>
/// Computes standard SHA-256 digests and Base64 representations for ZATCA invoice payloads.
/// </summary>
public static class InvoiceHashCalculator
{
    /// <summary>
    /// Computes the SHA-256 digest and Base64 string for a canonicalized XML or invoice payload string.
    /// </summary>
    public static InvoiceHashResult ComputeHash(string payload)
    {
        if (string.IsNullOrWhiteSpace(payload))
        {
            throw new ArgumentException("Payload to hash cannot be null or whitespace.", nameof(payload));
        }

        var payloadBytes = Encoding.UTF8.GetBytes(payload);
        return ComputeHash(payloadBytes);
    }

    /// <summary>
    /// Computes the SHA-256 digest and Base64 string for a raw byte payload.
    /// </summary>
    public static InvoiceHashResult ComputeHash(byte[] payloadBytes)
    {
        ArgumentNullException.ThrowIfNull(payloadBytes);
        if (payloadBytes.Length == 0)
        {
            throw new ArgumentException("Payload bytes cannot be empty.", nameof(payloadBytes));
        }

        var digest = SHA256.HashData(payloadBytes);
        var base64 = Convert.ToBase64String(digest);
        return new InvoiceHashResult(digest, base64);
    }
}

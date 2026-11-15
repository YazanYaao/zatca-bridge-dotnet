using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using ZatcaBridge.Domain.ValueObjects;

namespace ZatcaBridge.Domain.Cryptography;

/// <summary>
/// Encodes and decodes ZATCA TLV (Tag-Length-Value) structures to/from binary bytes and Base64 strings.
/// </summary>
public static class TlvEncoder
{
    /// <summary>
    /// Encodes a sequence of TLV tags into a raw byte array.
    /// </summary>
    public static byte[] Encode(IEnumerable<TlvTag> tags)
    {
        ArgumentNullException.ThrowIfNull(tags);

        using var memoryStream = new MemoryStream();
        foreach (var tag in tags)
        {
            var bytes = tag.ToBytes();
            memoryStream.Write(bytes, 0, bytes.Length);
        }

        return memoryStream.ToArray();
    }

    /// <summary>
    /// Encodes a sequence of TLV tags into a Base64 string for QR codes.
    /// </summary>
    public static string EncodeToBase64(IEnumerable<TlvTag> tags)
    {
        var bytes = Encode(tags);
        return Convert.ToBase64String(bytes);
    }

    /// <summary>
    /// Encodes a <see cref="ZatcaQrCodeData"/> object into a Base64 string for QR codes.
    /// </summary>
    public static string EncodeToBase64(ZatcaQrCodeData qrData)
    {
        ArgumentNullException.ThrowIfNull(qrData);
        return EncodeToBase64(qrData.ToTags());
    }

    /// <summary>
    /// Decodes a raw TLV byte array back into a list of <see cref="TlvTag"/> objects.
    /// </summary>
    public static IReadOnlyList<TlvTag> Decode(byte[] bytes)
    {
        ArgumentNullException.ThrowIfNull(bytes);

        var list = new List<TlvTag>();
        int index = 0;

        while (index < bytes.Length)
        {
            if (index + 2 > bytes.Length)
            {
                throw new ArgumentException("Truncated TLV header.", nameof(bytes));
            }

            byte tagNumber = bytes[index++];
            int length = bytes[index++];

            if (index + length > bytes.Length)
            {
                throw new ArgumentException($"Truncated TLV value for tag {tagNumber}. Expected {length} bytes but only {bytes.Length - index} remain.", nameof(bytes));
            }

            var valueBytes = new byte[length];
            Buffer.BlockCopy(bytes, index, valueBytes, 0, length);
            index += length;

            list.Add(TlvTag.FromBytes(tagNumber, valueBytes));
        }

        return list.AsReadOnly();
    }

    /// <summary>
    /// Decodes a Base64 encoded TLV string back into a list of <see cref="TlvTag"/> objects.
    /// </summary>
    public static IReadOnlyList<TlvTag> Decode(string base64)
    {
        if (string.IsNullOrWhiteSpace(base64))
        {
            throw new ArgumentException("Base64 string cannot be null or whitespace.", nameof(base64));
        }

        var bytes = Convert.FromBase64String(base64);
        return Decode(bytes);
    }
}

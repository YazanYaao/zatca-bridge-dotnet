using System;
using System.Globalization;
using System.Text;

namespace ZatcaBridge.Domain.Cryptography;

/// <summary>
/// Represents a single Tag-Length-Value (TLV) field according to ZATCA E-Invoicing specifications.
/// </summary>
public sealed record TlvTag
{
    public byte TagNumber { get; }
    public byte[] Value { get; }
    public int Length => Value.Length;

    public TlvTag(byte tagNumber, byte[] value)
    {
        if (tagNumber == 0)
        {
            throw new ArgumentOutOfRangeException(nameof(tagNumber), "Tag number must be greater than zero.");
        }

        ArgumentNullException.ThrowIfNull(value);

        if (value.Length > 255)
        {
            throw new ArgumentException("TLV value byte length cannot exceed 255 bytes.", nameof(value));
        }

        TagNumber = tagNumber;
        Value = (byte[])value.Clone();
    }

    public static TlvTag FromBytes(byte tagNumber, byte[] value) =>
        new(tagNumber, value);

    public static TlvTag FromUtf8(byte tagNumber, string value)
    {
        ArgumentNullException.ThrowIfNull(value);
        return new(tagNumber, Encoding.UTF8.GetBytes(value));
    }

    public static TlvTag FromDecimal(byte tagNumber, decimal value)
    {
        // ZATCA requires 2 decimal places with invariant culture (dot separator)
        var stringValue = value.ToString("0.00", CultureInfo.InvariantCulture);
        return FromUtf8(tagNumber, stringValue);
    }

    public static TlvTag FromDateTimeOffset(byte tagNumber, DateTimeOffset value)
    {
        // Format ISO-8601 UTC string: yyyy-MM-ddTHH:mm:ssZ
        var isoString = value.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ", CultureInfo.InvariantCulture);
        return FromUtf8(tagNumber, isoString);
    }

    public byte[] ToBytes()
    {
        var result = new byte[2 + Value.Length];
        result[0] = TagNumber;
        result[1] = (byte)Value.Length;
        Buffer.BlockCopy(Value, 0, result, 2, Value.Length);
        return result;
    }
}

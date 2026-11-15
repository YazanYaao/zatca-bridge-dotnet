using System;

namespace ZatcaBridge.Domain.ValueObjects;

/// <summary>
/// Value object representing the SHA-256 hash of the preceding invoice in the cryptographic hash chain.
/// </summary>
public sealed record PreviousInvoiceHash
{
    /// <summary>
    /// Official ZATCA standard genesis previous invoice hash (Base64 representation of SHA-256 of ASCII "0").
    /// </summary>
    public const string GenesisHashConstant = "NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI4NjliMDExMzg0NmNmNmIx";

    public string Value { get; }

    public bool IsGenesis => Value == GenesisHashConstant;

    public static PreviousInvoiceHash Genesis => new(GenesisHashConstant, isGenesis: true);

    public static PreviousInvoiceHash FromHash(string base64Hash)
    {
        if (string.IsNullOrWhiteSpace(base64Hash))
        {
            throw new ArgumentException("Previous invoice hash cannot be null or whitespace.", nameof(base64Hash));
        }

        var trimmed = base64Hash.Trim();
        try
        {
            _ = Convert.FromBase64String(trimmed);
        }
        catch (FormatException ex)
        {
            throw new ArgumentException($"Invalid Base64 string for previous invoice hash: '{base64Hash}'.", nameof(base64Hash), ex);
        }

        return new PreviousInvoiceHash(trimmed, isGenesis: trimmed == GenesisHashConstant);
    }

    private PreviousInvoiceHash(string value, bool isGenesis)
    {
        Value = value;
    }

    public override string ToString() => Value;
}

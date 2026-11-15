using System;

namespace ZatcaBridge.Domain.ValueObjects;

/// <summary>
/// Encapsulates a link in the cryptographic invoice hash chain, combining ICV, PreviousInvoiceHash, and the computed Invoice Hash.
/// </summary>
public sealed record HashChainRecord
{
    public InvoiceCounterValue Icv { get; }
    public PreviousInvoiceHash PreviousHash { get; }
    public string InvoiceHash { get; }

    public HashChainRecord(InvoiceCounterValue icv, PreviousInvoiceHash previousHash, string invoiceHash)
    {
        ArgumentNullException.ThrowIfNull(icv);
        ArgumentNullException.ThrowIfNull(previousHash);

        if (string.IsNullOrWhiteSpace(invoiceHash))
        {
            throw new ArgumentException("Invoice hash cannot be null or whitespace.", nameof(invoiceHash));
        }

        Icv = icv;
        PreviousHash = previousHash;
        InvoiceHash = invoiceHash.Trim();
    }

    /// <summary>
    /// Creates the genesis record (ICV = 1, PreviousHash = ZATCA standard genesis hash).
    /// </summary>
    public static HashChainRecord CreateGenesis(string invoiceHash)
    {
        return new HashChainRecord(
            InvoiceCounterValue.Genesis,
            PreviousInvoiceHash.Genesis,
            invoiceHash
        );
    }

    /// <summary>
    /// Creates the next sequential record chained to this record.
    /// </summary>
    public HashChainRecord CreateNext(string nextInvoiceHash)
    {
        return new HashChainRecord(
            Icv.Next(),
            PreviousInvoiceHash.FromHash(InvoiceHash),
            nextInvoiceHash
        );
    }

    /// <summary>
    /// Validates that <paramref name="nextRecord"/> is a valid, untampered consecutive link in the hash chain.
    /// </summary>
    public void ValidateNext(HashChainRecord nextRecord)
    {
        ArgumentNullException.ThrowIfNull(nextRecord);

        // Invariant 1: Sequential ICV increment (+1)
        Icv.EnsureNext(nextRecord.Icv);

        // Invariant 2: Hash link (Next record's PreviousInvoiceHash must strictly match current InvoiceHash)
        if (!string.Equals(InvoiceHash, nextRecord.PreviousHash.Value, StringComparison.Ordinal))
        {
            throw new InvalidOperationException(
                $"Cryptographic hash chain break detected at ICV {nextRecord.Icv.Value}. " +
                $"Expected PreviousInvoiceHash to match previous invoice hash '{InvoiceHash}', " +
                $"but found '{nextRecord.PreviousHash.Value}'."
            );
        }
    }
}

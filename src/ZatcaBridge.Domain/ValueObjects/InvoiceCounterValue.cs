using System;

namespace ZatcaBridge.Domain.ValueObjects;

/// <summary>
/// Strictly sequential monotonic Invoice Counter Value (ICV) tracking per device/CSID.
/// </summary>
public sealed record InvoiceCounterValue
{
    public long Value { get; }

    public bool IsGenesis => Value == 1;

    public static InvoiceCounterValue Genesis => new(1);

    public InvoiceCounterValue(long value)
    {
        if (value <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(value), "Invoice Counter Value (ICV) must be greater than zero.");
        }

        Value = value;
    }

    /// <summary>
    /// Computes the next monotonic sequential ICV (strictly +1).
    /// </summary>
    public InvoiceCounterValue Next() => new(Value + 1);

    /// <summary>
    /// Validates that the provided ICV is strictly the immediate sequential successor (+1).
    /// </summary>
    public void EnsureNext(InvoiceCounterValue next)
    {
        ArgumentNullException.ThrowIfNull(next);

        if (next.Value != Value + 1)
        {
            throw new InvalidOperationException($"Broken ICV sequence. Expected next counter to be {Value + 1}, but received {next.Value}.");
        }
    }

    public override string ToString() => Value.ToString();
}

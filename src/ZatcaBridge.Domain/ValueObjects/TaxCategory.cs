using System;
using ZatcaBridge.Domain.Enums;

namespace ZatcaBridge.Domain.ValueObjects;

public sealed record TaxCategory
{
    public TaxCategoryCode Code { get; }
    public decimal Rate { get; }
    public string? ExemptionReasonCode { get; }
    public string? ExemptionReason { get; }

    public static TaxCategory Standard => new(TaxCategoryCode.Standard, 0.15m);
    public static TaxCategory ZeroRated => new(TaxCategoryCode.ZeroRated, 0.00m);
    public static TaxCategory OutOfScope => new(TaxCategoryCode.OutOfScope, 0.00m);

    public static TaxCategory Exempt(string exemptionReasonCode, string exemptionReason)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(exemptionReasonCode);
        ArgumentException.ThrowIfNullOrWhiteSpace(exemptionReason);
        return new(TaxCategoryCode.Exempt, 0.00m, exemptionReasonCode, exemptionReason);
    }

    public TaxCategory(TaxCategoryCode code, decimal rate = 0.15m, string? exemptionReasonCode = null, string? exemptionReason = null)
    {
        if (rate < 0m)
        {
            throw new ArgumentOutOfRangeException(nameof(rate), "Tax rate cannot be negative.");
        }

        // KSA Standard Rate default is 15% (0.15m)
        if (code == TaxCategoryCode.Standard && rate == 0m)
        {
            rate = 0.15m;
        }

        Code = code;
        Rate = rate;
        ExemptionReasonCode = exemptionReasonCode;
        ExemptionReason = exemptionReason;
    }
}

using System;

namespace ZatcaBridge.Domain.ValueObjects;

public sealed record LegalMonetaryTotal
{
    public decimal LineExtensionAmount { get; }
    public decimal TaxExclusiveAmount { get; }
    public decimal TaxInclusiveAmount { get; }
    public decimal AllowanceTotalAmount { get; }
    public decimal ChargeTotalAmount { get; }
    public decimal PrepaidAmount { get; }
    public decimal PayableRoundingAmount { get; }
    public decimal PayableAmount { get; }

    public LegalMonetaryTotal(
        decimal lineExtensionAmount,
        decimal taxExclusiveAmount,
        decimal taxInclusiveAmount,
        decimal allowanceTotalAmount,
        decimal chargeTotalAmount,
        decimal prepaidAmount,
        decimal payableRoundingAmount,
        decimal payableAmount)
    {
        // CRITICAL RULE 1: decimal precision with MidpointRounding.AwayFromZero
        LineExtensionAmount = decimal.Round(lineExtensionAmount, 2, MidpointRounding.AwayFromZero);
        TaxExclusiveAmount = decimal.Round(taxExclusiveAmount, 2, MidpointRounding.AwayFromZero);
        TaxInclusiveAmount = decimal.Round(taxInclusiveAmount, 2, MidpointRounding.AwayFromZero);
        AllowanceTotalAmount = decimal.Round(allowanceTotalAmount, 2, MidpointRounding.AwayFromZero);
        ChargeTotalAmount = decimal.Round(chargeTotalAmount, 2, MidpointRounding.AwayFromZero);
        PrepaidAmount = decimal.Round(prepaidAmount, 2, MidpointRounding.AwayFromZero);
        PayableRoundingAmount = decimal.Round(payableRoundingAmount, 2, MidpointRounding.AwayFromZero);
        PayableAmount = decimal.Round(payableAmount, 2, MidpointRounding.AwayFromZero);
    }
}

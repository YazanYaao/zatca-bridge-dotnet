import { InvoiceLineItemDto } from '../types/api';

/**
 * Implements C# MidpointRounding.AwayFromZero for financial precision.
 */
export function roundAwayFromZero(num: number, decimals: number = 2): number {
  const factor = Math.pow(10, decimals);
  const n = num * factor;
  const sign = n < 0 ? -1 : 1;
  const abs = Math.abs(n);
  const integer = Math.floor(abs);
  const fraction = abs - integer;

  // Midpoint check with epsilon tolerance for JS floating point
  let rounded: number;
  if (fraction >= 0.5 - 1e-9) {
    rounded = integer + 1;
  } else {
    rounded = integer;
  }

  return (sign * rounded) / factor;
}

export interface CalculatedLineItem {
  lineExtensionAmount: number;
  taxAmount: number;
  totalAmount: number;
  taxRate: number;
}

export function calculateLineItem(item: InvoiceLineItemDto): CalculatedLineItem {
  const qty = Number(item.quantity) || 0;
  const price = Number(item.unitPrice) || 0;
  const discount = Number(item.discountAmount) || 0;

  const rawExtension = qty * price - discount;
  const lineExtensionAmount = roundAwayFromZero(Math.max(0, rawExtension), 2);

  let rate = 0.15;
  if (item.taxCategory.code === 'ZeroRated' || item.taxCategory.code === 'Exempt' || item.taxCategory.code === 'OutOfScope') {
    rate = 0.00;
  } else if (item.taxCategory.rate !== undefined && item.taxCategory.rate !== null) {
    rate = Number(item.taxCategory.rate);
  }

  const taxAmount = roundAwayFromZero(lineExtensionAmount * rate, 2);
  const totalAmount = roundAwayFromZero(lineExtensionAmount + taxAmount, 2);

  return {
    lineExtensionAmount,
    taxAmount,
    totalAmount,
    taxRate: rate,
  };
}

export interface InvoiceFinancialSummary {
  lineExtensionTotal: number;
  taxExclusiveAmount: number;
  taxInclusiveAmount: number;
  totalTaxAmount: number;
  payableAmount: number;
  taxSubtotals: {
    categoryCode: string;
    rate: number;
    taxableAmount: number;
    taxAmount: number;
  }[];
}

export function calculateInvoiceTotals(
  items: InvoiceLineItemDto[],
  allowanceTotal: number = 0,
  chargeTotal: number = 0,
  prepaidAmount: number = 0,
  payableRoundingAmount: number = 0
): InvoiceFinancialSummary {
  let lineExtensionTotal = 0;
  let totalTax = 0;

  const subtotalsMap = new Map<string, { rate: number; taxable: number; tax: number }>();

  items.forEach((item) => {
    const calc = calculateLineItem(item);
    lineExtensionTotal = roundAwayFromZero(lineExtensionTotal + calc.lineExtensionAmount, 2);
    totalTax = roundAwayFromZero(totalTax + calc.taxAmount, 2);

    const key = `${item.taxCategory.code}_${calc.taxRate}`;
    const existing = subtotalsMap.get(key) || { rate: calc.taxRate, taxable: 0, tax: 0 };
    existing.taxable = roundAwayFromZero(existing.taxable + calc.lineExtensionAmount, 2);
    existing.tax = roundAwayFromZero(existing.tax + calc.taxAmount, 2);
    subtotalsMap.set(key, existing);
  });

  const allowance = Number(allowanceTotal) || 0;
  const charge = Number(chargeTotal) || 0;
  const prepaid = Number(prepaidAmount) || 0;
  const rounding = Number(payableRoundingAmount) || 0;

  const taxExclusiveAmount = roundAwayFromZero(lineExtensionTotal - allowance + charge, 2);
  const taxInclusiveAmount = roundAwayFromZero(taxExclusiveAmount + totalTax, 2);
  const payableAmount = roundAwayFromZero(taxInclusiveAmount - prepaid + rounding, 2);

  const taxSubtotals = Array.from(subtotalsMap.entries()).map(([key, val]) => ({
    categoryCode: key.split('_')[0],
    rate: val.rate,
    taxableAmount: val.taxable,
    taxAmount: val.tax,
  }));

  return {
    lineExtensionTotal,
    taxExclusiveAmount,
    taxInclusiveAmount,
    totalTaxAmount: totalTax,
    payableAmount,
    taxSubtotals,
  };
}

export function formatSar(amount: number): string {
  return new Intl.NumberFormat('en-SA', {
    style: 'currency',
    currency: 'SAR',
    minimumFractionDigits: 2,
    maximumFractionDigits: 2,
  }).format(amount);
}

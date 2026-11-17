import React, { useState, useMemo } from 'react';
import { useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  Plus, 
  Trash2, 
  Send, 
  Sparkles, 
  Calculator, 
  Loader2, 
  Building2, 
  User, 
  FileCheck,
  AlertCircle
} from 'lucide-react';
import { api } from '../../api/client';
import { 
  IssueInvoiceRequest, 
  InvoiceLineItemDto, 
  PartyDto, 
  TaxCategoryCode, 
  IssueInvoiceResult 
} from '../../types/api';
import { calculateInvoiceTotals, formatSar } from '../../utils/calculations';

interface InvoiceBuilderTabProps {
  onInvoiceCreated: (result: IssueInvoiceResult) => void;
}

const DEFAULT_SELLER: PartyDto = {
  name: 'Acme Saudi Supplies Ltd',
  vatRegistrationNumber: '310123456700003',
  street: 'King Abdulaziz Road',
  buildingNumber: '1234',
  postalZone: '12211',
  city: 'Riyadh',
  district: 'Al-Olaya',
  countryCode: 'SA',
};

const DEFAULT_BUYER: PartyDto = {
  name: 'Global Retail Solutions Co',
  vatRegistrationNumber: '320987654300003',
  street: 'Tahlia Street',
  buildingNumber: '5678',
  postalZone: '12334',
  city: 'Riyadh',
  district: 'Al-Sulaimaniyah',
  countryCode: 'SA',
};

export const InvoiceBuilderTab: React.FC<InvoiceBuilderTabProps> = ({ onInvoiceCreated }) => {
  const queryClient = useQueryClient();
  const today = new Date().toISOString().split('T')[0];

  const [invoiceNumber, setInvoiceNumber] = useState(`INV-${Date.now().toString().slice(-6)}`);
  const [issueDate, setIssueDate] = useState(today);
  const [issueTime, setIssueTime] = useState('12:00:00');
  const [invoiceType, setInvoiceType] = useState<388 | 381 | 383 | 386>(388);
  const [transactionType, setTransactionType] = useState<0 | 1>(0); // 0 = B2B, 1 = B2C

  const [seller, setSeller] = useState<PartyDto>(DEFAULT_SELLER);
  const [buyer, setBuyer] = useState<PartyDto | null>(DEFAULT_BUYER);
  const [hasBuyer, setHasBuyer] = useState(true);

  const [lineItems, setLineItems] = useState<InvoiceLineItemDto[]>([
    {
      name: 'High-Performance Cloud Server',
      quantity: 1,
      unitPrice: 1000.0,
      discountAmount: 0.0,
      taxCategory: { code: 'Standard', rate: 0.15 },
    },
    {
      name: 'Professional Consulting Hours',
      quantity: 4,
      unitPrice: 250.0,
      discountAmount: 50.0,
      taxCategory: { code: 'Standard', rate: 0.15 },
    },
  ]);

  const [allowanceTotal, setAllowanceTotal] = useState(0);
  const [chargeTotal, setChargeTotal] = useState(0);
  const [prepaidAmount, setPrepaidAmount] = useState(0);
  const [payableRoundingAmount, setPayableRoundingAmount] = useState(0);

  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Live financial invariant calculations
  const totals = useMemo(() => {
    return calculateInvoiceTotals(
      lineItems,
      allowanceTotal,
      chargeTotal,
      prepaidAmount,
      payableRoundingAmount
    );
  }, [lineItems, allowanceTotal, chargeTotal, prepaidAmount, payableRoundingAmount]);

  const issueMutation = useMutation({
    mutationFn: api.issueInvoice,
    onSuccess: (result) => {
      setErrorMessage(null);
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
      queryClient.invalidateQueries({ queryKey: ['chainHistory'] });
      queryClient.invalidateQueries({ queryKey: ['outbox'] });
      onInvoiceCreated(result);
    },
    onError: (err: any) => {
      setErrorMessage(err.detail || err.message || 'Failed to issue invoice');
    },
  });

  // Presets Handlers
  const applyPreset = (presetName: string) => {
    setErrorMessage(null);
    const newNumber = `INV-${Date.now().toString().slice(-6)}`;
    setInvoiceNumber(newNumber);

    switch (presetName) {
      case 'b2b-standard':
        setInvoiceType(388);
        setTransactionType(0);
        setHasBuyer(true);
        setBuyer(DEFAULT_BUYER);
        setLineItems([
          {
            name: 'Enterprise Software Subscription',
            quantity: 2,
            unitPrice: 500.0,
            discountAmount: 50.0,
            taxCategory: { code: 'Standard', rate: 0.15 },
          },
          {
            name: 'API Support & Maintenance',
            quantity: 1,
            unitPrice: 200.0,
            discountAmount: 0.0,
            taxCategory: { code: 'Standard', rate: 0.15 },
          },
        ]);
        break;

      case 'b2c-simplified':
        setInvoiceType(388);
        setTransactionType(1);
        setHasBuyer(false);
        setBuyer(null);
        setLineItems([
          {
            name: 'Retail Electronics Purchase',
            quantity: 1,
            unitPrice: 350.0,
            discountAmount: 0.0,
            taxCategory: { code: 'Standard', rate: 0.15 },
          },
        ]);
        break;

      case 'zero-rated':
        setInvoiceType(388);
        setTransactionType(0);
        setHasBuyer(true);
        setBuyer({
          ...DEFAULT_BUYER,
          name: 'Overseas Logistics Partner LLC',
          countryCode: 'AE',
        });
        setLineItems([
          {
            name: 'International Freight Logistics',
            quantity: 1,
            unitPrice: 2500.0,
            discountAmount: 0.0,
            taxCategory: {
              code: 'ZeroRated',
              rate: 0.0,
              exemptionReasonCode: 'VATEX-SA-32',
              exemptionReason: 'Export of goods',
            },
          },
        ]);
        break;

      case 'exempt':
        setInvoiceType(388);
        setTransactionType(0);
        setHasBuyer(true);
        setBuyer(DEFAULT_BUYER);
        setLineItems([
          {
            name: 'Diagnostic Clinical Healthcare Service',
            quantity: 1,
            unitPrice: 800.0,
            discountAmount: 0.0,
            taxCategory: {
              code: 'Exempt',
              rate: 0.0,
              exemptionReasonCode: 'VATEX-SA-HEA',
              exemptionReason: 'Exempt healthcare service',
            },
          },
        ]);
        break;

      case 'credit-note':
        setInvoiceType(381);
        setTransactionType(0);
        setHasBuyer(true);
        setBuyer(DEFAULT_BUYER);
        setLineItems([
          {
            name: 'Returned Defective Hardware Modules',
            quantity: 2,
            unitPrice: 150.0,
            discountAmount: 0.0,
            taxCategory: { code: 'Standard', rate: 0.15 },
          },
        ]);
        break;
    }
  };

  const addLineItem = () => {
    setLineItems([
      ...lineItems,
      {
        name: `Service / Item #${lineItems.length + 1}`,
        quantity: 1,
        unitPrice: 100.0,
        discountAmount: 0.0,
        taxCategory: { code: 'Standard', rate: 0.15 },
      },
    ]);
  };

  const removeLineItem = (index: number) => {
    if (lineItems.length <= 1) return;
    setLineItems(lineItems.filter((_, i) => i !== index));
  };

  const updateLineItem = (index: number, updates: Partial<InvoiceLineItemDto>) => {
    setLineItems(
      lineItems.map((item, i) => {
        if (i !== index) return item;
        return { ...item, ...updates };
      })
    );
  };

  const updateTaxCategory = (index: number, code: TaxCategoryCode) => {
    let rate = 0.15;
    let exemptionReasonCode: string | undefined = undefined;
    let exemptionReason: string | undefined = undefined;

    if (code === 'ZeroRated') {
      rate = 0.0;
      exemptionReasonCode = 'VATEX-SA-32';
      exemptionReason = 'Export of goods';
    } else if (code === 'Exempt') {
      rate = 0.0;
      exemptionReasonCode = 'VATEX-SA-OOS';
      exemptionReason = 'Exempt';
    } else if (code === 'OutOfScope') {
      rate = 0.0;
    }

    updateLineItem(index, {
      taxCategory: {
        code,
        rate,
        exemptionReasonCode,
        exemptionReason,
      },
    });
  };

  const handleSubmit = (e: React.FormEvent) => {
    e.preventDefault();
    setErrorMessage(null);

    const payload: IssueInvoiceRequest = {
      invoiceNumber,
      issueDate,
      issueTime,
      invoiceType,
      transactionType,
      seller,
      buyer: hasBuyer ? buyer : null,
      lineItems,
      allowanceTotal,
      chargeTotal,
      prepaidAmount,
      payableRoundingAmount,
    };

    issueMutation.mutate(payload);
  };

  return (
    <div className="space-y-6">
      {/* 1-Click Scenario Presets Bar */}
      <div className="bg-slate-800/80 border border-slate-700/80 rounded-2xl p-4 shadow-xl">
        <div className="flex items-center space-x-2 text-xs font-semibold text-slate-400 uppercase tracking-wider mb-3">
          <Sparkles className="w-4 h-4 text-zatca-400" />
          <span>1-Click Taxpayer Test Scenarios</span>
        </div>
        <div className="flex flex-wrap gap-2">
          <button
            type="button"
            onClick={() => applyPreset('b2b-standard')}
            className="px-3.5 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-100 text-xs font-medium rounded-xl border border-slate-600 transition flex items-center space-x-1.5"
          >
            <span>📄 B2B Standard (15%)</span>
          </button>
          <button
            type="button"
            onClick={() => applyPreset('b2c-simplified')}
            className="px-3.5 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-100 text-xs font-medium rounded-xl border border-slate-600 transition flex items-center space-x-1.5"
          >
            <span>🧾 B2C Simplified Cash Sale (15%)</span>
          </button>
          <button
            type="button"
            onClick={() => applyPreset('zero-rated')}
            className="px-3.5 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-100 text-xs font-medium rounded-xl border border-slate-600 transition flex items-center space-x-1.5"
          >
            <span>🌐 Zero-Rated Export (0%)</span>
          </button>
          <button
            type="button"
            onClick={() => applyPreset('exempt')}
            className="px-3.5 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-100 text-xs font-medium rounded-xl border border-slate-600 transition flex items-center space-x-1.5"
          >
            <span>🏥 Exempt Healthcare (0%)</span>
          </button>
          <button
            type="button"
            onClick={() => applyPreset('credit-note')}
            className="px-3.5 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-100 text-xs font-medium rounded-xl border border-slate-600 transition flex items-center space-x-1.5"
          >
            <span>↩️ Credit Note (381)</span>
          </button>
        </div>
      </div>

      {/* Error alert */}
      {errorMessage && (
        <div className="bg-rose-950/60 border border-rose-800 text-rose-300 rounded-xl p-4 text-sm flex items-start space-x-3">
          <AlertCircle className="w-5 h-5 flex-shrink-0 text-rose-400 mt-0.5" />
          <div>
            <p className="font-semibold text-rose-200">Validation / Processing Error</p>
            <p>{errorMessage}</p>
          </div>
        </div>
      )}

      <form onSubmit={handleSubmit} className="space-y-6">
        {/* Invoice Metadata */}
        <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg">
          <h3 className="font-bold text-white text-base mb-4 flex items-center space-x-2">
            <FileCheck className="w-4 h-4 text-zatca-400" />
            <span>Invoice Identification & Mandate Type</span>
          </h3>

          <div className="grid grid-cols-1 sm:grid-cols-2 lg:grid-cols-5 gap-4">
            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Invoice Number</label>
              <input
                type="text"
                value={invoiceNumber}
                onChange={(e) => setInvoiceNumber(e.target.value)}
                required
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white font-mono focus:outline-none focus:border-zatca-500"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Issue Date</label>
              <input
                type="date"
                value={issueDate}
                onChange={(e) => setIssueDate(e.target.value)}
                required
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white focus:outline-none focus:border-zatca-500"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Issue Time</label>
              <input
                type="time"
                step="1"
                value={issueTime}
                onChange={(e) => setIssueTime(e.target.value)}
                required
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white focus:outline-none focus:border-zatca-500"
              />
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Document Type</label>
              <select
                value={invoiceType}
                onChange={(e) => setInvoiceType(Number(e.target.value) as any)}
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white focus:outline-none focus:border-zatca-500"
              >
                <option value={388}>388 - Standard Tax Invoice</option>
                <option value={381}>381 - Credit Note</option>
                <option value={383}>383 - Debit Note</option>
                <option value={386}>386 - Prepayment Invoice</option>
              </select>
            </div>

            <div>
              <label className="block text-xs font-medium text-slate-300 mb-1">Transaction Stream</label>
              <select
                value={transactionType}
                onChange={(e) => {
                  const val = Number(e.target.value) as 0 | 1;
                  setTransactionType(val);
                  if (val === 1) setHasBuyer(false);
                }}
                className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1.5 text-xs text-white focus:outline-none focus:border-zatca-500"
              >
                <option value={0}>B2B (Clearance Mandatory)</option>
                <option value={1}>B2C (Simplified 24hr Reporting)</option>
              </select>
            </div>
          </div>
        </div>

        {/* Parties (Seller & Buyer) */}
        <div className="grid grid-cols-1 lg:grid-cols-2 gap-6">
          {/* Seller */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-3">
            <h4 className="font-bold text-white text-sm flex items-center space-x-2">
              <Building2 className="w-4 h-4 text-zatca-400" />
              <span>Seller (AccountingSupplierParty)</span>
            </h4>
            <div className="grid grid-cols-2 gap-3">
              <div className="col-span-2">
                <label className="block text-[11px] text-slate-400 mb-0.5">Company Name</label>
                <input
                  type="text"
                  value={seller.name}
                  onChange={(e) => setSeller({ ...seller, name: e.target.value })}
                  required
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white"
                />
              </div>
              <div className="col-span-2">
                <label className="block text-[11px] text-slate-400 mb-0.5">VAT Registration Number (15 digits)</label>
                <input
                  type="text"
                  value={seller.vatRegistrationNumber}
                  onChange={(e) => setSeller({ ...seller, vatRegistrationNumber: e.target.value })}
                  required
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white font-mono"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">City</label>
                <input
                  type="text"
                  value={seller.city}
                  onChange={(e) => setSeller({ ...seller, city: e.target.value })}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">Postal Zone</label>
                <input
                  type="text"
                  value={seller.postalZone}
                  onChange={(e) => setSeller({ ...seller, postalZone: e.target.value })}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white font-mono"
                />
              </div>
            </div>
          </div>

          {/* Buyer */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-3">
            <div className="flex items-center justify-between">
              <h4 className="font-bold text-white text-sm flex items-center space-x-2">
                <User className="w-4 h-4 text-zatca-400" />
                <span>Buyer (AccountingCustomerParty)</span>
              </h4>
              <label className="flex items-center space-x-2 text-xs text-slate-400 cursor-pointer">
                <input
                  type="checkbox"
                  checked={hasBuyer}
                  onChange={(e) => setHasBuyer(e.target.checked)}
                  className="rounded bg-slate-900 border-slate-700 text-zatca-600 focus:ring-0"
                />
                <span>Include Buyer Party</span>
              </label>
            </div>

            {hasBuyer ? (
              <div className="grid grid-cols-2 gap-3">
                <div className="col-span-2">
                  <label className="block text-[11px] text-slate-400 mb-0.5">Buyer Legal Name</label>
                  <input
                    type="text"
                    value={buyer?.name || ''}
                    onChange={(e) => setBuyer({ ...(buyer || DEFAULT_BUYER), name: e.target.value })}
                    required={hasBuyer}
                    className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white"
                  />
                </div>
                <div className="col-span-2">
                  <label className="block text-[11px] text-slate-400 mb-0.5">Buyer VAT Number (15 digits)</label>
                  <input
                    type="text"
                    value={buyer?.vatRegistrationNumber || ''}
                    onChange={(e) => setBuyer({ ...(buyer || DEFAULT_BUYER), vatRegistrationNumber: e.target.value })}
                    required={hasBuyer}
                    className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white font-mono"
                  />
                </div>
                <div>
                  <label className="block text-[11px] text-slate-400 mb-0.5">City</label>
                  <input
                    type="text"
                    value={buyer?.city || ''}
                    onChange={(e) => setBuyer({ ...(buyer || DEFAULT_BUYER), city: e.target.value })}
                    className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white"
                  />
                </div>
                <div>
                  <label className="block text-[11px] text-slate-400 mb-0.5">Postal Zone</label>
                  <input
                    type="text"
                    value={buyer?.postalZone || ''}
                    onChange={(e) => setBuyer({ ...(buyer || DEFAULT_BUYER), postalZone: e.target.value })}
                    className="w-full bg-slate-900 border border-slate-700 rounded-lg px-3 py-1 text-xs text-white font-mono"
                  />
                </div>
              </div>
            ) : (
              <div className="py-8 text-center text-xs text-slate-500 border border-dashed border-slate-700 rounded-xl">
                Optional for B2C Simplified invoices. Buyer information omitted.
              </div>
            )}
          </div>
        </div>

        {/* Dynamic Line Items */}
        <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-4">
          <div className="flex items-center justify-between">
            <h4 className="font-bold text-white text-base">Invoice Line Items</h4>
            <button
              type="button"
              onClick={addLineItem}
              className="flex items-center space-x-1.5 px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs font-medium rounded-xl transition"
            >
              <Plus className="w-3.5 h-3.5 text-zatca-400" />
              <span>Add Line Item</span>
            </button>
          </div>

          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-900/60 text-slate-400 uppercase text-[10px] tracking-wider">
                <tr>
                  <th className="py-2.5 px-3">Item Description</th>
                  <th className="py-2.5 px-3 w-20">Qty</th>
                  <th className="py-2.5 px-3 w-28">Unit Price</th>
                  <th className="py-2.5 px-3 w-24">Discount</th>
                  <th className="py-2.5 px-3 w-36">Tax Category</th>
                  <th className="py-2.5 px-3 w-28 text-right">Tax (SAR)</th>
                  <th className="py-2.5 px-3 w-28 text-right">Total (SAR)</th>
                  <th className="py-2.5 px-2 w-10 text-center"></th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-800">
                {lineItems.map((item, idx) => {
                  const lineExt = Math.max(0, item.quantity * item.unitPrice - item.discountAmount);
                  const rate = item.taxCategory.code === 'Standard' ? (item.taxCategory.rate ?? 0.15) : 0;
                  const tax = lineExt * rate;
                  const total = lineExt + tax;

                  return (
                    <tr key={idx} className="hover:bg-slate-800/40">
                      <td className="py-2 px-3">
                        <input
                          type="text"
                          value={item.name}
                          onChange={(e) => updateLineItem(idx, { name: e.target.value })}
                          required
                          className="w-full bg-slate-900 border border-slate-700 rounded px-2.5 py-1 text-white text-xs"
                        />
                      </td>
                      <td className="py-2 px-3">
                        <input
                          type="number"
                          step="0.01"
                          min="0.01"
                          value={item.quantity}
                          onChange={(e) => updateLineItem(idx, { quantity: Number(e.target.value) })}
                          required
                          className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white text-xs font-mono"
                        />
                      </td>
                      <td className="py-2 px-3">
                        <input
                          type="number"
                          step="0.01"
                          min="0.01"
                          value={item.unitPrice}
                          onChange={(e) => updateLineItem(idx, { unitPrice: Number(e.target.value) })}
                          required
                          className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white text-xs font-mono"
                        />
                      </td>
                      <td className="py-2 px-3">
                        <input
                          type="number"
                          step="0.01"
                          min="0"
                          value={item.discountAmount}
                          onChange={(e) => updateLineItem(idx, { discountAmount: Number(e.target.value) })}
                          className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white text-xs font-mono"
                        />
                      </td>
                      <td className="py-2 px-3">
                        <select
                          value={item.taxCategory.code}
                          onChange={(e) => updateTaxCategory(idx, e.target.value as TaxCategoryCode)}
                          className="w-full bg-slate-900 border border-slate-700 rounded px-2 py-1 text-white text-xs"
                        >
                          <option value="Standard">Standard (15%)</option>
                          <option value="ZeroRated">Zero-Rated (0%)</option>
                          <option value="Exempt">Exempt (0%)</option>
                          <option value="OutOfScope">Out of Scope (0%)</option>
                        </select>
                      </td>
                      <td className="py-2 px-3 text-right font-mono text-slate-300">
                        {tax.toFixed(2)}
                      </td>
                      <td className="py-2 px-3 text-right font-mono font-semibold text-white">
                        {total.toFixed(2)}
                      </td>
                      <td className="py-2 px-2 text-center">
                        <button
                          type="button"
                          onClick={() => removeLineItem(idx)}
                          disabled={lineItems.length <= 1}
                          className="text-slate-500 hover:text-rose-400 disabled:opacity-30 transition"
                        >
                          <Trash2 className="w-4 h-4" />
                        </button>
                      </td>
                    </tr>
                  );
                })}
              </tbody>
            </table>
          </div>
        </div>

        {/* Live Financial Summary & Submit */}
        <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
          <div className="lg:col-span-2 bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-3">
            <h4 className="font-bold text-white text-sm">Invoice Allowances & Rounding (Optional)</h4>
            <div className="grid grid-cols-2 sm:grid-cols-4 gap-3">
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">Allowance Total (SAR)</label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={allowanceTotal}
                  onChange={(e) => setAllowanceTotal(Number(e.target.value))}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white font-mono"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">Charge Total (SAR)</label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={chargeTotal}
                  onChange={(e) => setChargeTotal(Number(e.target.value))}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white font-mono"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">Prepaid Amount (SAR)</label>
                <input
                  type="number"
                  step="0.01"
                  min="0"
                  value={prepaidAmount}
                  onChange={(e) => setPrepaidAmount(Number(e.target.value))}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white font-mono"
                />
              </div>
              <div>
                <label className="block text-[11px] text-slate-400 mb-0.5">Rounding Amount (SAR)</label>
                <input
                  type="number"
                  step="0.01"
                  value={payableRoundingAmount}
                  onChange={(e) => setPayableRoundingAmount(Number(e.target.value))}
                  className="w-full bg-slate-900 border border-slate-700 rounded-lg px-2.5 py-1 text-xs text-white font-mono"
                />
              </div>
            </div>
          </div>

          {/* Financial Calculation Card */}
          <div className="bg-gradient-to-br from-slate-800 to-slate-900 border border-slate-700 rounded-2xl p-5 shadow-xl flex flex-col justify-between">
            <div>
              <div className="flex items-center justify-between pb-3 border-b border-slate-700">
                <span className="font-bold text-white text-sm flex items-center space-x-1.5">
                  <Calculator className="w-4 h-4 text-zatca-400" />
                  <span>Calculated Totals</span>
                </span>
                <span className="text-[10px] uppercase font-bold text-zatca-400 tracking-wider">
                  AwayFromZero
                </span>
              </div>

              <div className="space-y-2 py-3 text-xs">
                <div className="flex justify-between text-slate-400">
                  <span>Line Extension Total:</span>
                  <span className="font-mono text-slate-200">{formatSar(totals.lineExtensionTotal)}</span>
                </div>
                <div className="flex justify-between text-slate-400">
                  <span>Tax Exclusive Amount:</span>
                  <span className="font-mono text-slate-200">{formatSar(totals.taxExclusiveAmount)}</span>
                </div>
                <div className="flex justify-between text-slate-400">
                  <span>Total VAT (Tax Amount):</span>
                  <span className="font-mono text-slate-200">{formatSar(totals.totalTaxAmount)}</span>
                </div>
                <div className="pt-2 border-t border-slate-700/60 flex justify-between items-center text-sm">
                  <span className="font-bold text-white">Gross Payable Amount:</span>
                  <span className="font-mono font-bold text-base text-zatca-400">
                    {formatSar(totals.payableAmount)}
                  </span>
                </div>
              </div>
            </div>

            <button
              type="submit"
              disabled={issueMutation.isPending}
              className="w-full py-3 bg-zatca-600 hover:bg-zatca-500 disabled:bg-slate-700 text-white font-bold text-sm rounded-xl transition flex items-center justify-center space-x-2 shadow-lg shadow-zatca-900/30"
            >
              {issueMutation.isPending ? (
                <>
                  <Loader2 className="w-4 h-4 animate-spin" />
                  <span>Signing & Staging Invoice...</span>
                </>
              ) : (
                <>
                  <Send className="w-4 h-4" />
                  <span>Issue & Cryptographically Sign</span>
                </>
              )}
            </button>
          </div>
        </div>
      </form>
    </div>
  );
};

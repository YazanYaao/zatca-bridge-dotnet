import React, { useEffect } from 'react';
import { QRCodeSVG } from 'qrcode.react';
import { Printer, X, ShieldCheck, CheckCircle2 } from 'lucide-react';
import { InvoiceDetailResponse } from '../types/api';
import { parseInvoiceXmlPayload, ParsedInvoiceDoc } from '../utils/xmlParser';
import { formatSar } from '../utils/calculations';

interface InvoicePdfTemplateProps {
  invoice: InvoiceDetailResponse;
  onClose: () => void;
}

export const InvoicePdfTemplate: React.FC<InvoicePdfTemplateProps> = ({ invoice, onClose }) => {
  const doc: ParsedInvoiceDoc = parseInvoiceXmlPayload(invoice);

  // Close on Escape key
  useEffect(() => {
    const handleKeyDown = (e: KeyboardEvent) => {
      if (e.key === 'Escape') {
        onClose();
      }
    };
    window.addEventListener('keydown', handleKeyDown);
    return () => window.removeEventListener('keydown', handleKeyDown);
  }, [onClose]);

  const handlePrint = () => {
    window.print();
  };

  return (
    <div className="fixed inset-0 z-50 bg-slate-950/85 backdrop-blur-sm overflow-y-auto p-2 sm:p-6 flex flex-col items-center">
      {/* Top Floating Control Toolbar (Hidden in Print) */}
      <div className="no-print sticky top-2 sm:top-4 z-50 w-full max-w-[210mm] mb-4 bg-slate-900/95 border border-slate-700/80 rounded-2xl p-3 shadow-2xl flex items-center justify-between gap-3 text-white backdrop-blur-md">
        <div className="flex items-center space-x-2.5 rtl:space-x-reverse px-2">
          <div className="w-8 h-8 rounded-lg bg-zatca-950 border border-zatca-700 flex items-center justify-center text-zatca-400">
            <ShieldCheck className="w-4 h-4" />
          </div>
          <div>
            <h3 className="text-xs sm:text-sm font-bold flex items-center gap-2">
              <span>{doc.invoiceTitleAr}</span>
              <span className="text-slate-400 font-normal">/ {doc.invoiceTitleEn}</span>
            </h3>
            <span className="text-[11px] text-slate-400 font-mono">
              {doc.invoiceNumber} (ICV #{doc.icv})
            </span>
          </div>
        </div>

        <div className="flex items-center space-x-2 rtl:space-x-reverse">
          <button
            onClick={handlePrint}
            className="flex items-center space-x-1.5 rtl:space-x-reverse px-4 py-2 bg-zatca-600 hover:bg-zatca-500 text-white rounded-xl text-xs sm:text-sm font-semibold transition shadow-lg shadow-zatca-950/40"
            title="طباعة / حفظ كـ PDF"
          >
            <Printer className="w-4 h-4" />
            <span>طباعة / حفظ PDF</span>
          </button>

          <button
            onClick={onClose}
            className="p-2 bg-slate-800 hover:bg-slate-700 text-slate-300 hover:text-white rounded-xl transition"
            title="إغلاق"
          >
            <X className="w-5 h-5" />
          </button>
        </div>
      </div>

      {/* A4 Printable Sheet Container */}
      <div
        id="zatca-invoice-printable"
        dir="rtl"
        className="w-full max-w-[210mm] min-h-[297mm] bg-white text-slate-900 shadow-2xl p-6 sm:p-10 rounded-sm font-arabic relative border border-slate-200 print:border-none print:shadow-none print:p-0 print:m-0 print:max-w-none print:w-full"
      >
        {/* Top Decorative Border */}
        <div className="h-2 w-full bg-gradient-to-r from-emerald-600 via-teal-600 to-emerald-700 rounded-t-sm mb-6 print:mb-4" />

        {/* 1. Header: Seller Info & Bilingual Identity */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-6 pb-6 border-b border-slate-200 text-xs">
          {/* Seller Arabic Details */}
          <div className="space-y-1 text-right">
            <h1 className="text-lg sm:text-xl font-extrabold text-slate-900 tracking-tight">
              {doc.seller.name}
            </h1>
            <p className="text-slate-600 font-medium">
              {doc.seller.buildingNumber}، {doc.seller.street}، {doc.seller.district}، {doc.seller.city} {doc.seller.postalZone}
            </p>
            <p className="text-slate-600">المملكة العربية السعودية</p>
            <div className="pt-2 space-y-0.5">
              <div className="flex items-center gap-2">
                <span className="font-bold text-slate-700">رقم السجل التجاري:</span>
                <span className="font-mono font-semibold text-slate-900">{doc.seller.crn}</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="font-bold text-emerald-800">الرقم الضريبي للمنشأة:</span>
                <span className="font-mono font-bold text-emerald-800 text-sm tracking-wider">
                  {doc.seller.vatNumber}
                </span>
              </div>
            </div>
          </div>

          {/* Seller English Details */}
          <div dir="ltr" className="space-y-1 text-left md:border-l md:border-slate-200 md:pl-6">
            <h2 className="text-base sm:text-lg font-bold text-slate-800">
              {doc.seller.name}
            </h2>
            <p className="text-slate-600">
              {doc.seller.buildingNumber}, {doc.seller.street}, {doc.seller.district}, {doc.seller.city} {doc.seller.postalZone}
            </p>
            <p className="text-slate-600">Kingdom of Saudi Arabia</p>
            <div className="pt-2 space-y-0.5 text-xs">
              <div className="flex items-center gap-2">
                <span className="font-bold text-slate-700">CR Number:</span>
                <span className="font-mono font-semibold text-slate-900">{doc.seller.crn}</span>
              </div>
              <div className="flex items-center gap-2">
                <span className="font-bold text-emerald-800">VAT Registration No:</span>
                <span className="font-mono font-bold text-emerald-800 text-sm tracking-wider">
                  {doc.seller.vatNumber}
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* 2. Document Title Banner */}
        <div className="my-6 text-center bg-slate-50 border-y-2 border-slate-800 py-3">
          <h2 className="text-xl sm:text-2xl font-black text-slate-900 tracking-wide">
            {doc.invoiceTitleAr}
          </h2>
          <span className="text-xs sm:text-sm font-semibold text-slate-600 uppercase tracking-widest block mt-0.5">
            {doc.invoiceTitleEn}
          </span>
        </div>

        {/* 3. Invoice Metadata & Buyer Information Grid */}
        <div className="grid grid-cols-1 md:grid-cols-2 gap-4 mb-6 text-xs">
          {/* Buyer Information Box */}
          <div className="border border-slate-300 rounded-lg p-3 bg-slate-50/60 space-y-2">
            <div className="border-b border-slate-200 pb-1.5 flex justify-between items-center">
              <span className="font-bold text-slate-900 text-sm">بيانات العميل / Customer Details</span>
              <span className="text-[10px] text-slate-500 font-medium">
                {doc.isB2B ? 'منشأة تجارية (B2B)' : 'فرد / مبسطة (B2C)'}
              </span>
            </div>

            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between">
                <span className="text-slate-600">اسم العميل / Name:</span>
                <span className="font-bold text-slate-900 text-left">{doc.buyer.name}</span>
              </div>

              <div className="flex justify-between">
                <span className="text-slate-600">الرقم الضريبي / VAT No:</span>
                <span className="font-mono font-bold text-slate-900">{doc.buyer.vatNumber}</span>
              </div>

              {doc.buyer.crn && (
                <div className="flex justify-between">
                  <span className="text-slate-600">السجل التجاري / CRN:</span>
                  <span className="font-mono font-semibold text-slate-900">{doc.buyer.crn}</span>
                </div>
              )}

              <div className="flex justify-between items-start pt-1">
                <span className="text-slate-600 whitespace-nowrap ml-2">العنوان / Address:</span>
                <span className="text-slate-800 text-left text-[11px] leading-tight">
                  {doc.buyer.fullAddress}
                </span>
              </div>
            </div>
          </div>

          {/* Invoice Metadata Box */}
          <div className="border border-slate-300 rounded-lg p-3 bg-slate-50/60 space-y-2">
            <div className="border-b border-slate-200 pb-1.5 flex justify-between items-center">
              <span className="font-bold text-slate-900 text-sm">تفاصيل الفاتورة / Invoice Details</span>
              <span className="text-[10px] font-mono bg-emerald-100 text-emerald-800 px-2 py-0.5 rounded font-bold">
                ICV #{doc.icv}
              </span>
            </div>

            <div className="space-y-1.5 text-xs">
              <div className="flex justify-between">
                <span className="text-slate-600">رقم الفاتورة / Invoice No:</span>
                <span className="font-mono font-extrabold text-slate-900">{doc.invoiceNumber}</span>
              </div>

              <div className="flex justify-between">
                <span className="text-slate-600">تاريخ الإصدار / Issue Date:</span>
                <span className="font-mono font-semibold text-slate-900">
                  {doc.issueDate} {doc.issueTime ? `• ${doc.issueTime}` : ''}
                </span>
              </div>

              <div className="flex justify-between">
                <span className="text-slate-600">تاريخ التوريد / Supply Date:</span>
                <span className="font-mono font-semibold text-slate-900">{doc.supplyDate}</span>
              </div>

              <div className="flex justify-between items-center pt-1 border-t border-slate-200">
                <span className="text-slate-600 whitespace-nowrap ml-2">UUID المعرف الفريد:</span>
                <span className="font-mono text-[9px] text-slate-600 truncate max-w-[210px]" title={doc.uuid}>
                  {doc.uuid}
                </span>
              </div>
            </div>
          </div>
        </div>

        {/* 4. Line Items Table */}
        <div className="mb-6 overflow-hidden rounded-lg border border-slate-300">
          <table className="w-full text-right text-xs border-collapse">
            <thead>
              <tr className="bg-slate-800 text-white font-bold text-[11px] leading-tight">
                <th className="py-2.5 px-2 text-center w-8">#</th>
                <th className="py-2.5 px-3">
                  <div>الوصف</div>
                  <div className="text-[9px] text-slate-300 font-normal">Description</div>
                </th>
                <th className="py-2.5 px-2 text-center w-14">
                  <div>الكمية</div>
                  <div className="text-[9px] text-slate-300 font-normal">Qty</div>
                </th>
                <th className="py-2.5 px-2 text-left w-24">
                  <div>سعر الوحدة</div>
                  <div className="text-[9px] text-slate-300 font-normal">Unit Price</div>
                </th>
                <th className="py-2.5 px-2 text-left w-24">
                  <div>المبلغ الخاضع</div>
                  <div className="text-[9px] text-slate-300 font-normal">Taxable Amt</div>
                </th>
                <th className="py-2.5 px-2 text-center w-14">
                  <div>الضريبة</div>
                  <div className="text-[9px] text-slate-300 font-normal">VAT %</div>
                </th>
                <th className="py-2.5 px-2 text-left w-24">
                  <div>مبلغ الضريبة</div>
                  <div className="text-[9px] text-slate-300 font-normal">VAT Amt</div>
                </th>
                <th className="py-2.5 px-3 text-left w-28">
                  <div>المجموع (ر.س)</div>
                  <div className="text-[9px] text-slate-300 font-normal">Total (SAR)</div>
                </th>
              </tr>
            </thead>
            <tbody className="divide-y divide-slate-200 font-medium text-slate-800">
              {doc.lineItems.map((item, idx) => (
                <tr key={item.id || idx} className="hover:bg-slate-50/80 break-inside-avoid">
                  <td className="py-3 px-2 text-center font-mono text-slate-500">{idx + 1}</td>
                  <td className="py-3 px-3">
                    <div className="font-semibold text-slate-900">{item.name}</div>
                  </td>
                  <td className="py-3 px-2 text-center font-mono">{item.quantity}</td>
                  <td className="py-3 px-2 text-left font-mono">{item.unitPrice.toFixed(2)}</td>
                  <td className="py-3 px-2 text-left font-mono">{item.taxableAmount.toFixed(2)}</td>
                  <td className="py-3 px-2 text-center font-mono text-slate-600">{item.taxPercent}%</td>
                  <td className="py-3 px-2 text-left font-mono">{item.taxAmount.toFixed(2)}</td>
                  <td className="py-3 px-3 text-left font-mono font-bold text-slate-900">
                    {item.totalWithTax.toFixed(2)}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>

        {/* 5. Totals & Official ZATCA QR Code Section (Side-by-Side) */}
        <div className="grid grid-cols-1 md:grid-cols-12 gap-6 items-start pb-6 border-b border-slate-200">
          {/* ZATCA Phase 2 Official QR Code Box (Col 5) */}
          <div className="md:col-span-5 flex flex-col items-center justify-center p-3 bg-slate-50 border border-slate-300 rounded-xl text-center">
            <div className="p-2 bg-white rounded-lg shadow-sm border border-slate-200">
              {doc.qrCodeBase64 ? (
                <QRCodeSVG
                  value={doc.qrCodeBase64}
                  size={140}
                  level="M"
                  includeMargin={false}
                />
              ) : (
                <div className="w-[140px] h-[140px] flex items-center justify-center text-slate-400 text-xs">
                  لا يوجد رمز QR
                </div>
              )}
            </div>

            {/* Mandatory ZATCA Regulatory Bilingual Caption */}
            <div className="mt-2.5 max-w-[210px] space-y-0.5">
              <p className="text-[10px] font-bold text-slate-700 leading-tight">
                تم ترميز هذا الرمز وفقاً لمتطلبات هيئة الزكاة والضريبة والجمارك للفوترة الإلكترونية
              </p>
              <p className="text-[9px] text-slate-500 leading-tight" dir="ltr">
                This QR code is encoded as per ZATCA e-invoicing requirements
              </p>
            </div>
          </div>

          {/* Financial Totals Summary Box (Col 7) */}
          <div className="md:col-span-7 bg-slate-50 border border-slate-300 rounded-xl p-4 space-y-2.5 text-xs">
            <div className="flex justify-between items-center text-slate-700 pb-2 border-b border-slate-200">
              <div>
                <span className="font-semibold block">المجموع الخاضع للضريبة (غير شامل ضريبة القيمة المضافة)</span>
                <span className="text-[10px] text-slate-500">Taxable Amount (Excl. VAT)</span>
              </div>
              <span className="font-mono font-bold text-sm text-slate-900">
                {formatSar(doc.subtotal)}
              </span>
            </div>

            <div className="flex justify-between items-center text-slate-700 pb-2 border-b border-slate-200">
              <div>
                <span className="font-semibold block">إجمالي ضريبة القيمة المضافة (15%)</span>
                <span className="text-[10px] text-slate-500">Total VAT (15%)</span>
              </div>
              <span className="font-mono font-bold text-sm text-slate-900">
                {formatSar(doc.totalTax)}
              </span>
            </div>

            <div className="flex justify-between items-center bg-emerald-50 -mx-4 -mb-4 p-4 rounded-b-xl border-t border-emerald-200 text-emerald-950">
              <div>
                <span className="font-extrabold text-sm block">الإجمالي شامل ضريبة القيمة المضافة</span>
                <span className="text-[11px] text-emerald-800 font-medium">Grand Total (Incl. VAT)</span>
              </div>
              <div className="text-left">
                <span className="font-mono font-black text-lg text-emerald-900 block">
                  {formatSar(doc.grandTotal)}
                </span>
                <span className="text-[10px] text-emerald-700 font-medium">ريال سعودي / SAR</span>
              </div>
            </div>
          </div>
        </div>

        {/* 6. Footer: Clearance Status & Hash Audit */}
        <div className="mt-4 pt-2 text-[10px] text-slate-500 flex flex-col sm:flex-row items-center justify-between gap-2">
          <div className="flex items-center space-x-2 rtl:space-x-reverse">
            <span className="inline-flex items-center gap-1 font-semibold text-emerald-700 bg-emerald-50 px-2 py-0.5 rounded border border-emerald-200">
              <CheckCircle2 className="w-3 h-3" />
              <span>{doc.outboxStatus === 'PROCESSED' ? 'معتمدة ومطابقة لهيئة الزكاة / ZATCA COMPLIANT' : doc.outboxStatus}</span>
            </span>
            <span className="text-slate-400 font-mono truncate max-w-[260px]" title={doc.hash}>
              SHA-256: {doc.hash ? `${doc.hash.substring(0, 18)}...` : 'N/A'}
            </span>
          </div>

          <div className="text-slate-400">
            صفحة 1 من 1 • Page 1 of 1
          </div>
        </div>
      </div>
    </div>
  );
};

import React, { useState, useMemo } from 'react';
import { useQuery } from '@tanstack/react-query';
import { QRCodeSVG } from 'qrcode.react';
import { 
  QrCode, 
  Code, 
  Copy, 
  Download, 
  Check, 
  Hash, 
  Layers, 
  Calendar, 
  DollarSign, 
  ShieldCheck, 
  ChevronDown, 
  ChevronRight,
  Info,
  Printer
} from 'lucide-react';
import { api } from '../../api/client';
import { decodeTlvBase64, DecodedTlvTag } from '../../utils/tlvDecoder';
import { formatSar } from '../../utils/calculations';
import { InvoicePdfTemplate } from '../InvoicePdfTemplate';

interface CryptographicInspectorTabProps {
  selectedInvoiceId?: string;
  onSelectInvoiceId: (id: string) => void;
}

export const CryptographicInspectorTab: React.FC<CryptographicInspectorTabProps> = ({
  selectedInvoiceId,
  onSelectInvoiceId,
}) => {
  const [copiedXml, setCopiedXml] = useState(false);
  const [copiedHash, setCopiedHash] = useState(false);
  const [copiedQr, setCopiedQr] = useState(false);
  const [isPdfModalOpen, setIsPdfModalOpen] = useState(false);
  const [expandedTags, setExpandedTags] = useState<Record<number, boolean>>({
    1: true,
    2: true,
    3: true,
    4: true,
    5: true,
    6: true,
    7: true,
  });

  // Query invoices list to populate invoice selector dropdown
  const { data: invoicesData } = useQuery({
    queryKey: ['invoices'],
    queryFn: () => api.getInvoices(1, 50, 'desc'),
  });

  // If no invoice selected, select the first available invoice
  const activeId = selectedInvoiceId || invoicesData?.items[0]?.id;

  // Query detailed invoice data
  const { data: invoice, isLoading } = useQuery({
    queryKey: ['invoice', activeId],
    queryFn: () => api.getInvoiceById(activeId!),
    enabled: !!activeId,
  });

  // Decode TLV Base64
  const decodedTlv = useMemo(() => {
    if (!invoice?.qrCodeBase64) return { tags: [] };
    return decodeTlvBase64(invoice.qrCodeBase64);
  }, [invoice?.qrCodeBase64]);

  const toggleTag = (tag: number) => {
    setExpandedTags((prev) => ({ ...prev, [tag]: !prev[tag] }));
  };

  const copyToClipboard = (text: string, type: 'xml' | 'hash' | 'qr') => {
    navigator.clipboard.writeText(text);
    if (type === 'xml') {
      setCopiedXml(true);
      setTimeout(() => setCopiedXml(false), 2000);
    } else if (type === 'hash') {
      setCopiedHash(true);
      setTimeout(() => setCopiedHash(false), 2000);
    } else if (type === 'qr') {
      setCopiedQr(true);
      setTimeout(() => setCopiedQr(false), 2000);
    }
  };

  const downloadXml = () => {
    if (!invoice?.xmlPayload) return;
    const blob = new Blob([invoice.xmlPayload], { type: 'application/xml;charset=utf-8' });
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url;
    link.download = `${invoice.invoiceNumber || 'zatca_invoice'}.xml`;
    document.body.appendChild(link);
    link.click();
    document.body.removeChild(link);
    URL.revokeObjectURL(url);
  };

  if (!activeId && (!invoicesData || invoicesData.items.length === 0)) {
    return (
      <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-12 text-center shadow-lg space-y-4">
        <div className="w-16 h-16 rounded-2xl bg-slate-800 text-slate-500 mx-auto flex items-center justify-center">
          <QrCode className="w-8 h-8" />
        </div>
        <h3 className="text-lg font-bold text-white">No Invoices Available for Inspection</h3>
        <p className="text-sm text-slate-400 max-w-md mx-auto">
          Issue your first invoice in the <strong className="text-zatca-400">Invoice Builder</strong> tab to inspect its real-time cryptographic hash, XAdES-BES digital signature, and decoded TLV QR code.
        </p>
      </div>
    );
  }

  return (
    <div className="space-y-6">
      {/* Top Invoice Selector & Summary Bar */}
      <div className="bg-slate-800/80 border border-slate-700/80 rounded-2xl p-4 shadow-xl flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
        <div className="flex items-center space-x-3 w-full md:w-auto">
          <div className="w-10 h-10 rounded-xl bg-zatca-950 border border-zatca-800 flex items-center justify-center text-zatca-400">
            <QrCode className="w-5 h-5" />
          </div>
          <div>
            <div className="flex items-center space-x-2">
              <span className="font-bold text-white text-base">Cryptographic Inspector</span>
              <span className="px-2 py-0.5 rounded-full text-[11px] font-semibold bg-slate-700 text-slate-300">
                ICV #{invoice?.icv ?? '...'}
              </span>
            </div>
            <p className="text-xs text-slate-400">
              {invoice?.invoiceType} • {invoice?.transactionTypeName} • {invoice ? formatSar(invoice.payableAmount) : ''}
            </p>
          </div>
        </div>

        {/* Invoice Dropdown Switcher */}
        {invoicesData && invoicesData.items.length > 0 && (
          <div className="flex items-center space-x-2 w-full md:w-auto">
            <label className="text-xs text-slate-400 whitespace-nowrap">Switch Invoice:</label>
            <select
              value={activeId}
              onChange={(e) => onSelectInvoiceId(e.target.value)}
              className="bg-slate-900 border border-slate-700 rounded-xl px-3 py-1.5 text-xs text-white focus:outline-none focus:border-zatca-500 w-full md:w-64"
            >
              {invoicesData.items.map((item) => (
                <option key={item.id} value={item.id}>
                  ICV #{item.icv} - {item.invoiceNumber} ({item.transactionTypeName})
                </option>
              ))}
            </select>

            {invoice && (
              <button
                onClick={() => setIsPdfModalOpen(true)}
                className="flex items-center space-x-1.5 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold rounded-xl transition shadow whitespace-nowrap"
                title="معاينة وطباعة الفاتورة A4 / Print & Export PDF"
              >
                <Printer className="w-3.5 h-3.5" />
                <span>طباعة / PDF</span>
              </button>
            )}
          </div>
        )}
      </div>

      {/* Main Split Grid: QR & TLV Inspector | XML Viewer */}
      <div className="grid grid-cols-1 lg:grid-cols-12 gap-6">
        {/* Left Col (5 cols): QR Matrix & Binary TLV Decoder */}
        <div className="lg:col-span-5 space-y-6">
          {/* QR Matrix Card */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-6 shadow-lg flex flex-col items-center text-center">
            <div className="p-3 bg-white rounded-2xl shadow-2xl mb-4">
              {invoice?.qrCodeBase64 ? (
                <QRCodeSVG
                  value={invoice.qrCodeBase64}
                  size={200}
                  level="M"
                  includeMargin={false}
                />
              ) : (
                <div className="w-48 h-48 flex items-center justify-center text-slate-400 text-xs">
                  No QR Code
                </div>
              )}
            </div>

            <h4 className="font-bold text-white text-sm">ZATCA Phase 2 TLV QR Code</h4>
            <p className="text-xs text-slate-400 mt-1 mb-3">
              Encodes binary Tag-Length-Value (Tags 1–9) converted to Base64.
            </p>

            <button
              onClick={() => copyToClipboard(invoice?.qrCodeBase64 || '', 'qr')}
              className="flex items-center space-x-1.5 px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs font-medium rounded-xl transition"
            >
              {copiedQr ? (
                <>
                  <Check className="w-3.5 h-3.5 text-zatca-400" />
                  <span>Copied Base64 QR!</span>
                </>
              ) : (
                <>
                  <Copy className="w-3.5 h-3.5 text-slate-400" />
                  <span>Copy Base64 Payload</span>
                </>
              )}
            </button>
          </div>

          {/* Binary TLV 1-9 Inspector */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-4">
            <div className="flex items-center justify-between pb-3 border-b border-slate-700">
              <h4 className="font-bold text-white text-sm flex items-center space-x-2">
                <Layers className="w-4 h-4 text-zatca-400" />
                <span>Binary TLV Tags (1–9) Breakdown</span>
              </h4>
              <span className="text-[11px] font-semibold text-zatca-400 bg-zatca-950 px-2.5 py-0.5 rounded-full border border-zatca-800">
                {decodedTlv.tags.length} Tags Decoded
              </span>
            </div>

            <div className="space-y-2.5">
              {decodedTlv.tags.map((tag: DecodedTlvTag) => {
                const isExpanded = !!expandedTags[tag.tag];
                return (
                  <div
                    key={tag.tag}
                    className="border border-slate-700/70 rounded-xl overflow-hidden bg-slate-900/60 transition"
                  >
                    <button
                      type="button"
                      onClick={() => toggleTag(tag.tag)}
                      className="w-full px-3 py-2.5 flex items-center justify-between text-left hover:bg-slate-800/40"
                    >
                      <div className="flex items-center space-x-2.5">
                        <span className="w-6 h-6 rounded-md bg-zatca-950 border border-zatca-800 text-zatca-400 text-xs font-bold flex items-center justify-center font-mono">
                          {tag.tag}
                        </span>
                        <div>
                          <span className="text-xs font-semibold text-white block">{tag.name}</span>
                          <span className="text-[10px] text-slate-400 block">{tag.description}</span>
                        </div>
                      </div>
                      <div className="flex items-center space-x-2">
                        <span className="text-[10px] font-mono text-slate-400 bg-slate-800 px-2 py-0.5 rounded">
                          {tag.rawLength} B
                        </span>
                        {isExpanded ? (
                          <ChevronDown className="w-4 h-4 text-slate-400" />
                        ) : (
                          <ChevronRight className="w-4 h-4 text-slate-400" />
                        )}
                      </div>
                    </button>

                    {isExpanded && (
                      <div className="px-3 pb-3 pt-1 border-t border-slate-800 text-xs space-y-1.5 font-mono">
                        {tag.stringValue && (
                          <div className="bg-slate-950/80 p-2 rounded text-slate-200 break-all select-all">
                            {tag.stringValue}
                          </div>
                        )}
                        {tag.hexValue && (
                          <div className="text-[11px] text-slate-400 bg-slate-950/80 p-2 rounded break-all select-all">
                            <span className="text-zatca-400 text-[10px] uppercase font-sans font-bold block mb-1">
                              Hex Representation:
                            </span>
                            {tag.hexValue}
                          </div>
                        )}
                        {tag.base64Value && (
                          <div className="text-[11px] text-slate-400 bg-slate-950/80 p-2 rounded break-all select-all">
                            <span className="text-blue-400 text-[10px] uppercase font-sans font-bold block mb-1">
                              Base64 Digest:
                            </span>
                            {tag.base64Value}
                          </div>
                        )}
                      </div>
                    )}
                  </div>
                );
              })}
            </div>
          </div>
        </div>

        {/* Right Col (7 cols): UBL 2.1 XML Viewer & Cryptographic Chaining */}
        <div className="lg:col-span-7 space-y-6">
          {/* Cryptographic Linkage Info Card */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg space-y-3">
            <h4 className="font-bold text-white text-sm flex items-center space-x-2">
              <Hash className="w-4 h-4 text-zatca-400" />
              <span>SHA-256 Digest & Hash Chain Continuity</span>
            </h4>

            <div className="space-y-2 text-xs">
              <div className="bg-slate-900/80 border border-slate-700 rounded-xl p-3 space-y-1">
                <div className="flex items-center justify-between text-slate-400">
                  <span>Current Invoice Hash (SHA-256 Digest):</span>
                  <button
                    onClick={() => copyToClipboard(invoice?.invoiceHash || '', 'hash')}
                    className="text-zatca-400 hover:text-zatca-300 flex items-center space-x-1"
                  >
                    {copiedHash ? <Check className="w-3.5 h-3.5" /> : <Copy className="w-3.5 h-3.5" />}
                    <span>{copiedHash ? 'Copied' : 'Copy'}</span>
                  </button>
                </div>
                <div className="font-mono text-white text-[11px] break-all select-all">
                  {invoice?.invoiceHash || 'N/A'}
                </div>
              </div>

              <div className="bg-slate-900/80 border border-slate-700 rounded-xl p-3 space-y-1">
                <span className="text-slate-400 block">Previous Invoice Hash (PIH):</span>
                <div className="font-mono text-slate-300 text-[11px] break-all select-all">
                  {invoice?.previousInvoiceHash || 'Genesis Hash (NWZl...)'}
                </div>
              </div>
            </div>
          </div>

          {/* Formatted UBL 2.1 XML Viewer */}
          <div className="bg-slate-800/60 border border-slate-700/80 rounded-2xl p-5 shadow-lg flex flex-col h-[580px]">
            <div className="flex items-center justify-between pb-3 border-b border-slate-700 mb-3">
              <div className="flex items-center space-x-2">
                <Code className="w-4 h-4 text-zatca-400" />
                <h4 className="font-bold text-white text-sm">ZATCA UBL 2.1 XML Payload</h4>
              </div>

              <div className="flex items-center space-x-2">
                <button
                  onClick={() => setIsPdfModalOpen(true)}
                  className="flex items-center space-x-1 px-3 py-1.5 bg-emerald-600 hover:bg-emerald-500 text-white text-xs font-semibold rounded-lg transition shadow"
                  title="معاينة وطباعة الفاتورة الضريبية / Export PDF"
                >
                  <Printer className="w-3.5 h-3.5" />
                  <span>طباعة / تصدير PDF</span>
                </button>

                <button
                  onClick={() => copyToClipboard(invoice?.xmlPayload || '', 'xml')}
                  className="flex items-center space-x-1 px-3 py-1.5 bg-slate-700 hover:bg-slate-600 text-slate-200 text-xs font-medium rounded-lg transition"
                >
                  {copiedXml ? (
                    <>
                      <Check className="w-3.5 h-3.5 text-zatca-400" />
                      <span>Copied!</span>
                    </>
                  ) : (
                    <>
                      <Copy className="w-3.5 h-3.5 text-slate-400" />
                      <span>Copy XML</span>
                    </>
                  )}
                </button>

                <button
                  onClick={downloadXml}
                  className="flex items-center space-x-1 px-3 py-1.5 bg-zatca-700 hover:bg-zatca-600 text-white text-xs font-medium rounded-lg transition"
                >
                  <Download className="w-3.5 h-3.5" />
                  <span>Download .xml</span>
                </button>
              </div>
            </div>

            {/* XML Text Area */}
            <div className="flex-1 overflow-auto bg-slate-950/90 rounded-xl p-4 font-mono text-[11px] text-emerald-400/90 leading-relaxed scrollbar-thin select-all">
              <pre>{invoice?.xmlPayload || '<!-- No XML payload found -->'}</pre>
            </div>
          </div>
        </div>
      </div>

      {/* A4 Tax Invoice Print & PDF Modal */}
      {isPdfModalOpen && invoice && (
        <InvoicePdfTemplate
          invoice={invoice}
          onClose={() => setIsPdfModalOpen(false)}
        />
      )}
    </div>
  );
};

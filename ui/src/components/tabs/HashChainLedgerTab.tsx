import React, { useState } from 'react';
import { useQuery, useMutation, useQueryClient } from '@tanstack/react-query';
import { 
  GitCommit, 
  Send, 
  RefreshCw, 
  Clock, 
  CheckCircle2, 
  XCircle, 
  ArrowRight, 
  Layers, 
  AlertTriangle,
  Loader2,
  ExternalLink
} from 'lucide-react';
import { api } from '../../api/client';
import { ChainRecordItemDto, OutboxMessageItemDto } from '../../types/api';

interface HashChainLedgerTabProps {
  onSelectInvoiceHash?: (hash: string) => void;
}

export const HashChainLedgerTab: React.FC<HashChainLedgerTabProps> = ({ onSelectInvoiceHash }) => {
  const queryClient = useQueryClient();
  const [autoRefresh, setAutoRefresh] = useState(true);
  const [dispatchMessage, setDispatchMessage] = useState<string | null>(null);

  // Poll chain history & outbox every 3 seconds when autoRefresh is enabled
  const { data: chainData, isLoading: isChainLoading, refetch: refetchChain } = useQuery({
    queryKey: ['chainHistory'],
    queryFn: api.getChainHistory,
    refetchInterval: autoRefresh ? 3000 : false,
  });

  const { data: outboxData, isLoading: isOutboxLoading, refetch: refetchOutbox } = useQuery({
    queryKey: ['outbox'],
    queryFn: () => api.getOutbox(undefined, 50),
    refetchInterval: autoRefresh ? 3000 : false,
  });

  // Outbox process mutation
  const processMutation = useMutation({
    mutationFn: () => api.processOutbox(10),
    onSuccess: (data) => {
      setDispatchMessage(`Successfully processed ${data.processedCount} outbox message(s) with ZATCA Sandbox.`);
      queryClient.invalidateQueries({ queryKey: ['outbox'] });
      queryClient.invalidateQueries({ queryKey: ['invoices'] });
    },
    onError: (err: any) => {
      setDispatchMessage(`Dispatch failed: ${err.detail || err.message}`);
    },
  });

  const handleManualRefresh = () => {
    refetchChain();
    refetchOutbox();
  };

  const getStatusBadge = (statusName: string) => {
    switch (statusName) {
      case 'Completed':
      case 'Cleared':
      case 'Reported':
        return (
          <span className="inline-flex items-center space-x-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#ECFDF5] text-[#065F46] border border-[#A7F3D0]">
            <CheckCircle2 className="w-3 h-3 text-[#065F46]" />
            <span>{statusName}</span>
          </span>
        );
      case 'Processing':
        return (
          <span className="inline-flex items-center space-x-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-sky-50 text-sky-800 border border-sky-200">
            <RefreshCw className="w-3 h-3 animate-spin text-sky-700" />
            <span>Processing</span>
          </span>
        );
      case 'Failed':
      case 'Rejected':
        return (
          <span className="inline-flex items-center space-x-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#FEF2F2] text-[#991B1B] border border-[#FECACA]">
            <XCircle className="w-3 h-3 text-[#991B1B]" />
            <span>{statusName}</span>
          </span>
        );
      default:
        return (
          <span className="inline-flex items-center space-x-1 px-2.5 py-0.5 rounded-full text-[11px] font-semibold bg-[#FFFBEB] text-[#92400E] border border-[#FDE68A]">
            <Clock className="w-3 h-3 text-[#92400E]" />
            <span>Pending</span>
          </span>
        );
    }
  };

  const chainRecords = chainData?.items || [];
  const outboxItems = outboxData?.items || [];

  return (
    <div className="space-y-6">
      {/* Top Controls & Auto-Refresh Header */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-4 shadow-sm flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
        <div>
          <h3 className="font-bold text-slate-900 text-base flex items-center space-x-2">
            <GitCommit className="w-5 h-5 text-emerald-700" />
            <span>Cryptographic Ledger & Outbox Dispatcher</span>
          </h3>
          <p className="text-xs text-slate-500 mt-0.5">
            Real-time visual SHA-256 hash chaining timeline and transactional outbox clearance monitor.
          </p>
        </div>

        <div className="flex items-center space-x-3 w-full md:w-auto">
          {/* Auto Refresh Toggle */}
          <label className="flex items-center space-x-2 text-xs text-slate-700 font-medium cursor-pointer bg-slate-50 border border-slate-200 px-3 py-1.5 rounded-xl">
            <input
              type="checkbox"
              checked={autoRefresh}
              onChange={(e) => setAutoRefresh(e.target.checked)}
              className="rounded border-slate-300 text-emerald-700 focus:ring-emerald-500"
            />
            <span>Auto-Refresh (3s)</span>
          </label>

          <button
            onClick={handleManualRefresh}
            className="p-2 bg-slate-50 hover:bg-slate-100 text-slate-700 rounded-xl transition border border-slate-200 shadow-xs"
            title="Refresh Ledger"
          >
            <RefreshCw className="w-4 h-4" />
          </button>

          <button
            onClick={() => processMutation.mutate()}
            disabled={processMutation.isPending}
            className="flex items-center space-x-2 px-4 py-2 bg-emerald-700 hover:bg-emerald-800 disabled:bg-slate-100 disabled:text-slate-400 text-white text-xs font-semibold rounded-xl transition shadow-xs whitespace-nowrap"
          >
            {processMutation.isPending ? (
              <>
                <Loader2 className="w-3.5 h-3.5 animate-spin" />
                <span>Dispatching Batch...</span>
              </>
            ) : (
              <>
                <Send className="w-3.5 h-3.5" />
                <span>Dispatch Outbox Now</span>
              </>
            )}
          </button>
        </div>
      </div>

      {/* Dispatch feedback message */}
      {dispatchMessage && (
        <div className="bg-emerald-50 border border-emerald-200 text-emerald-900 rounded-xl p-3 text-xs flex items-center justify-between shadow-xs">
          <span>{dispatchMessage}</span>
          <button
            onClick={() => setDispatchMessage(null)}
            className="text-emerald-700 hover:text-emerald-900 text-xs font-semibold ml-4"
          >
            Dismiss
          </button>
        </div>
      )}

      {/* Section 1: Visual Hash Chain Timeline */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-5 shadow-sm space-y-4">
        <div className="flex items-center justify-between pb-3 border-b border-slate-100">
          <div className="flex items-center space-x-2">
            <Layers className="w-4 h-4 text-emerald-700" />
            <h4 className="font-bold text-slate-900 text-sm">Sequential Hash Chain Timeline</h4>
          </div>
          <span className="text-xs font-mono tabular-nums text-slate-500">
            Total Blocks: {chainRecords.length}
          </span>
        </div>

        {chainRecords.length === 0 ? (
          <div className="py-8 text-center text-xs text-slate-500 border border-dashed border-slate-200 bg-slate-50/50 rounded-xl">
            Hash chain is empty. Genesis block will be minted on first invoice creation.
          </div>
        ) : (
          <div className="space-y-3 overflow-x-auto pb-2">
            {chainRecords.map((record: ChainRecordItemDto, index: number) => {
              const isGenesis = record.icv === 1;
              const priorRecord = index > 0 ? chainRecords[index - 1] : null;
              const isContinuityValid = isGenesis || (priorRecord && record.previousHash === priorRecord.invoiceHash);

              return (
                <div
                  key={record.icv}
                  className="bg-slate-50/80 border border-slate-200 rounded-xl p-3.5 flex flex-col md:flex-row items-start md:items-center justify-between gap-3 font-mono text-xs hover:border-slate-300 transition"
                >
                  <div className="flex items-center space-x-3">
                    <span className="w-8 h-8 rounded-lg bg-emerald-50 border border-emerald-200 text-emerald-800 font-bold flex items-center justify-center text-xs font-sans">
                      #{record.icv}
                    </span>

                    <div className="space-y-1">
                      <div className="flex items-center space-x-2">
                        <span className="text-slate-500 font-sans text-[11px]">Invoice Hash:</span>
                        <span className="text-slate-900 font-bold select-all tabular-nums">
                          {record.invoiceHash.slice(0, 16)}...{record.invoiceHash.slice(-8)}
                        </span>
                      </div>
                      <div className="flex items-center space-x-2 text-[11px] text-slate-500">
                        <span className="font-sans">Previous Hash (PIH):</span>
                        <span className="text-slate-700 select-all tabular-nums">
                          {record.previousHash.slice(0, 14)}...{record.previousHash.slice(-6)}
                        </span>
                      </div>
                    </div>
                  </div>

                  <div className="flex items-center space-x-3 w-full md:w-auto justify-between md:justify-end">
                    <span className="text-[11px] text-slate-500 font-sans">
                      {new Date(record.createdAt).toLocaleTimeString()}
                    </span>

                    <span
                      className={`px-2.5 py-0.5 rounded-full text-[10px] font-sans font-bold flex items-center space-x-1 ${
                        isContinuityValid
                          ? 'bg-[#ECFDF5] text-[#065F46] border border-[#A7F3D0]'
                          : 'bg-[#FEF2F2] text-[#991B1B] border border-[#FECACA]'
                      }`}
                    >
                      <CheckCircle2 className="w-3 h-3" />
                      <span>{isGenesis ? 'GENESIS BLOCK' : 'CHAINED VALID'}</span>
                    </span>
                  </div>
                </div>
              );
            })}
          </div>
        )}
      </div>

      {/* Section 2: Outbox Messages Queue */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-5 shadow-sm space-y-4">
        <div className="flex items-center justify-between pb-3 border-b border-slate-100">
          <div className="flex items-center space-x-2">
            <Send className="w-4 h-4 text-emerald-700" />
            <h4 className="font-bold text-slate-900 text-sm">Transactional Outbox Queue</h4>
          </div>
          <span className="text-xs font-mono tabular-nums text-slate-500">
            Queue Depth: {outboxItems.length}
          </span>
        </div>

        {outboxItems.length === 0 ? (
          <div className="py-8 text-center text-xs text-slate-500 border border-dashed border-slate-200 bg-slate-50/50 rounded-xl">
            Outbox queue is empty.
          </div>
        ) : (
          <div className="overflow-x-auto">
            <table className="w-full text-left text-xs">
              <thead className="bg-slate-50 text-slate-600 uppercase text-[10px] tracking-wider font-semibold border-b border-slate-200 font-sans">
                <tr>
                  <th className="py-2.5 px-3">ICV</th>
                  <th className="py-2.5 px-3">Operation</th>
                  <th className="py-2.5 px-3">Status</th>
                  <th className="py-2.5 px-3">Invoice Digest</th>
                  <th className="py-2.5 px-3">Retries</th>
                  <th className="py-2.5 px-3">Created</th>
                  <th className="py-2.5 px-3">Error / Note</th>
                </tr>
              </thead>
              <tbody className="divide-y divide-slate-100">
                {outboxItems.map((msg: OutboxMessageItemDto) => (
                  <tr key={msg.id} className="hover:bg-slate-50/70 border-b border-slate-100 transition">
                    <td className="py-2.5 px-3 font-mono tabular-nums font-bold text-slate-900">
                      #{msg.invoiceCounterValue}
                    </td>
                    <td className="py-2.5 px-3 font-medium text-slate-700">
                      {msg.typeName}
                    </td>
                    <td className="py-2.5 px-3">
                      {getStatusBadge(msg.statusName)}
                    </td>
                    <td className="py-2.5 px-3 font-mono tabular-nums text-[11px] text-slate-600">
                      {msg.invoiceHash.slice(0, 12)}...
                    </td>
                    <td className="py-2.5 px-3 font-mono tabular-nums text-slate-600">
                      {msg.retryCount}
                    </td>
                    <td className="py-2.5 px-3 text-[11px] text-slate-500">
                      {new Date(msg.createdAt).toLocaleTimeString()}
                    </td>
                    <td className="py-2.5 px-3 text-[11px] text-rose-700 max-w-xs truncate" title={msg.errorMessage || ''}>
                      {msg.errorMessage || '—'}
                    </td>
                  </tr>
                ))}
              </tbody>
            </table>
          </div>
        )}
      </div>
    </div>
  );
};

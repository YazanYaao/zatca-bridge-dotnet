import React, { useState } from 'react';
import { QueryClient, QueryClientProvider, useQuery } from '@tanstack/react-query';
import { Navigation, ActiveTab } from './components/Navigation';
import { OnboardingTab } from './components/tabs/OnboardingTab';
import { InvoiceBuilderTab } from './components/tabs/InvoiceBuilderTab';
import { CryptographicInspectorTab } from './components/tabs/CryptographicInspectorTab';
import { HashChainLedgerTab } from './components/tabs/HashChainLedgerTab';
import { api } from './api/client';
import { IssueInvoiceResult } from './types/api';
import { CheckCircle2, X } from 'lucide-react';

const queryClient = new QueryClient({
  defaultOptions: {
    queries: {
      refetchOnWindowFocus: false,
      staleTime: 5000,
    },
  },
});

const PlaygroundApp: React.FC = () => {
  const [activeTab, setActiveTab] = useState<ActiveTab>('builder');
  const [selectedInvoiceId, setSelectedInvoiceId] = useState<string | undefined>(undefined);
  const [toastMessage, setToastMessage] = useState<{ title: string; detail: string } | null>(null);

  // Poll onboarding status for active CSID badge
  const { data: onboardingStatus } = useQuery({
    queryKey: ['onboardingStatus'],
    queryFn: api.getOnboardingStatus,
  });

  const handleInvoiceCreated = (result: IssueInvoiceResult) => {
    setSelectedInvoiceId(result.invoiceId);
    setToastMessage({
      title: 'Invoice Issued & Signed!',
      detail: `Invoice ${result.invoiceNumber} (ICV #${result.invoiceCounterValue}) generated with SHA-256 hash chaining.`,
    });
    setActiveTab('inspector');
  };

  return (
    <div className="min-h-screen bg-slate-950 text-slate-100 flex flex-col">
      {/* Navigation Header */}
      <Navigation
        activeTab={activeTab}
        onTabChange={setActiveTab}
        onboardingStatus={onboardingStatus}
      />

      {/* Main Content Area */}
      <main className="flex-1 max-w-7xl w-full mx-auto px-4 sm:px-6 lg:px-8 py-6">
        {/* Toast Notification */}
        {toastMessage && (
          <div className="mb-6 bg-zatca-950/90 border border-zatca-700/80 rounded-2xl p-4 shadow-2xl flex items-start justify-between text-zatca-300 animate-in fade-in slide-in-from-top-2">
            <div className="flex items-center space-x-3">
              <div className="w-8 h-8 rounded-xl bg-zatca-900 border border-zatca-700 flex items-center justify-center text-zatca-400">
                <CheckCircle2 className="w-5 h-5" />
              </div>
              <div>
                <h4 className="font-bold text-white text-sm">{toastMessage.title}</h4>
                <p className="text-xs text-slate-300 mt-0.5">{toastMessage.detail}</p>
              </div>
            </div>
            <button
              onClick={() => setToastMessage(null)}
              className="text-slate-400 hover:text-white p-1 rounded-lg"
            >
              <X className="w-4 h-4" />
            </button>
          </div>
        )}

        {/* Tab Views */}
        {activeTab === 'onboarding' && <OnboardingTab />}
        {activeTab === 'builder' && (
          <InvoiceBuilderTab onInvoiceCreated={handleInvoiceCreated} />
        )}
        {activeTab === 'inspector' && (
          <CryptographicInspectorTab
            selectedInvoiceId={selectedInvoiceId}
            onSelectInvoiceId={setSelectedInvoiceId}
          />
        )}
        {activeTab === 'ledger' && (
          <HashChainLedgerTab
            onSelectInvoiceHash={() => setActiveTab('inspector')}
          />
        )}
      </main>

      {/* Footer */}
      <footer className="border-t border-slate-900 py-4 bg-slate-950/80 text-center text-xs text-slate-500">
        <p>
          ZatcaBridge • Compliant with ZATCA Phase 2 (Fatoora) • .NET 10 Minimal APIs & React Vite
        </p>
      </footer>
    </div>
  );
};

export default function App() {
  return (
    <QueryClientProvider client={queryClient}>
      <PlaygroundApp />
    </QueryClientProvider>
  );
}

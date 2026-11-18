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
    <div className="min-h-screen bg-slate-50 text-slate-900 flex flex-col antialiased selection:bg-emerald-100 selection:text-emerald-900">
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
          <div className="mb-6 bg-white border border-emerald-200/90 rounded-2xl p-4 shadow-sm flex items-start justify-between text-slate-800 animate-in fade-in slide-in-from-top-2">
            <div className="flex items-center space-x-3">
              <div className="w-9 h-9 rounded-xl bg-emerald-50 border border-emerald-200 flex items-center justify-center text-emerald-700 flex-shrink-0">
                <CheckCircle2 className="w-5 h-5" />
              </div>
              <div>
                <h4 className="font-semibold text-slate-900 text-sm tracking-tight">{toastMessage.title}</h4>
                <p className="text-xs text-slate-600 mt-0.5">{toastMessage.detail}</p>
              </div>
            </div>
            <button
              onClick={() => setToastMessage(null)}
              className="text-slate-400 hover:text-slate-700 p-1 rounded-lg transition"
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
      <footer className="border-t border-slate-200/80 py-4 bg-white text-xs text-slate-500">
        <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8 flex flex-col sm:flex-row items-center justify-between gap-2">
          <span className="font-medium text-slate-600">ZatcaBridge • Phase 2 Fatoora Engine</span>
          <span>UBL 2.1 XML • ECDSA secp256k1 • SHA-256 Hash Chain • ASP.NET Core & React</span>
        </div>
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

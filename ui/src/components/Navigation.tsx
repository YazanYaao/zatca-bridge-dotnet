import React from 'react';
import { 
  ShieldCheck, 
  FileText, 
  Cpu, 
  GitCommit, 
  ExternalLink,
  CheckCircle2,
  AlertCircle
} from 'lucide-react';
import { OnboardingStatusResponse } from '../types/api';

export type ActiveTab = 'onboarding' | 'builder' | 'inspector' | 'ledger';

interface NavigationProps {
  activeTab: ActiveTab;
  onTabChange: (tab: ActiveTab) => void;
  onboardingStatus?: OnboardingStatusResponse;
}

export const Navigation: React.FC<NavigationProps> = ({
  activeTab,
  onTabChange,
  onboardingStatus,
}) => {
  const tabs: { id: ActiveTab; label: string; icon: React.FC<{ className?: string }> }[] = [
    { id: 'onboarding', label: '1. Onboarding & CSID', icon: ShieldCheck },
    { id: 'builder', label: '2. Invoice Builder & Presets', icon: FileText },
    { id: 'inspector', label: '3. Cryptographic Inspector & QR', icon: Cpu },
    { id: 'ledger', label: '4. Hash Chain & Outbox Ledger', icon: GitCommit },
  ];

  const isCsidActive = onboardingStatus?.isActive;
  const envName = onboardingStatus?.environment || 'Sandbox';

  return (
    <header className="bg-slate-900 border-b border-slate-800/90 sticky top-0 z-50 shadow-sm">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          {/* Logo & Brand Identity */}
          <div className="flex items-center space-x-3">
            <div className="w-9 h-9 rounded-xl bg-emerald-700 flex items-center justify-center shadow-sm text-white">
              <ShieldCheck className="w-5 h-5 text-white" />
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <span className="font-bold text-base text-white tracking-tight">ZatcaBridge</span>
                <span className="px-2 py-0.5 text-[11px] font-semibold rounded-full bg-emerald-950 text-emerald-400 border border-emerald-800">
                  Phase 2 Fatoora
                </span>
                <span className="px-2 py-0.5 text-[11px] font-medium rounded-full bg-amber-950/80 text-amber-300 border border-amber-800">
                  {envName}
                </span>
              </div>
              <p className="text-[11px] text-slate-400">Enterprise FinTech & ZATCA Compliance Hub</p>
            </div>
          </div>

          {/* KPI Summary Strip & Active CSID Status */}
          <div className="hidden lg:flex items-center space-x-4">
            <div className="flex items-center space-x-3 text-xs text-slate-400 border-r border-slate-800 pr-4">
              <div className="flex items-center space-x-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
                <span className="font-mono text-slate-300 text-[11px]">UBL 2.1</span>
              </div>
              <div className="flex items-center space-x-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
                <span className="font-mono text-slate-300 text-[11px]">ECDSA secp256k1</span>
              </div>
              <div className="flex items-center space-x-1.5">
                <span className="w-1.5 h-1.5 rounded-full bg-emerald-400"></span>
                <span className="font-mono text-slate-300 text-[11px]">SHA-256 Chain</span>
              </div>
            </div>

            <div className="flex items-center space-x-2">
              <div
                className={`flex items-center space-x-2 px-3 py-1 rounded-full text-xs font-medium border ${
                  isCsidActive
                    ? 'bg-emerald-950 text-emerald-300 border-emerald-800'
                    : 'bg-amber-950 text-amber-300 border-amber-800'
                }`}
              >
                {isCsidActive ? (
                  <>
                    <CheckCircle2 className="w-3.5 h-3.5 text-emerald-400" />
                    <span className="font-semibold">CSID Active</span>
                  </>
                ) : (
                  <>
                    <AlertCircle className="w-3.5 h-3.5 text-amber-400" />
                    <span>Unsigned Mode (No CSID)</span>
                  </>
                )}
              </div>
              <a
                href="https://zatca.gov.sa"
                target="_blank"
                rel="noreferrer"
                className="p-1.5 text-slate-400 hover:text-slate-200 transition-colors"
                title="ZATCA Developer Portal"
              >
                <ExternalLink className="w-4 h-4" />
              </a>
            </div>
          </div>
        </div>

        {/* Tab Navigation */}
        <div className="flex space-x-1 overflow-x-auto pb-1 scrollbar-none border-t border-slate-800 pt-1">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => onTabChange(tab.id)}
                className={`flex items-center space-x-2 px-4 py-2 text-xs font-semibold rounded-lg transition-all whitespace-nowrap ${
                  isActive
                    ? 'bg-emerald-700 text-white shadow-sm'
                    : 'text-slate-400 hover:text-slate-200 hover:bg-slate-800/60'
                }`}
              >
                <Icon className={`w-4 h-4 ${isActive ? 'text-white' : 'text-slate-400'}`} />
                <span>{tab.label}</span>
              </button>
            );
          })}
        </div>
      </div>
    </header>
  );
};

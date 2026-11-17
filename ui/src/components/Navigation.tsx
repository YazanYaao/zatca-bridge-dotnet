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

  return (
    <header className="bg-slate-900 border-b border-slate-800 sticky top-0 z-50">
      <div className="max-w-7xl mx-auto px-4 sm:px-6 lg:px-8">
        <div className="flex items-center justify-between h-16">
          {/* Logo & Brand */}
          <div className="flex items-center space-x-3">
            <div className="w-10 h-10 rounded-xl bg-gradient-to-tr from-zatca-700 to-zatca-500 flex items-center justify-center shadow-lg shadow-zatca-900/30">
              <ShieldCheck className="w-6 h-6 text-white" />
            </div>
            <div>
              <div className="flex items-center space-x-2">
                <span className="font-bold text-lg text-white tracking-tight">ZatcaBridge</span>
                <span className="px-2 py-0.5 text-xs font-semibold rounded-full bg-zatca-950 text-zatca-400 border border-zatca-800">
                  Phase 2 Fatoora
                </span>
              </div>
              <p className="text-xs text-slate-400">Sandbox Playground & Compliance Explorer</p>
            </div>
          </div>

          {/* Active CSID Status Pill */}
          <div className="hidden md:flex items-center space-x-2">
            <div
              className={`flex items-center space-x-2 px-3 py-1 rounded-full text-xs font-medium border ${
                isCsidActive
                  ? 'bg-zatca-950 text-zatca-400 border-zatca-800'
                  : 'bg-amber-950/60 text-amber-400 border-amber-800/80'
              }`}
            >
              {isCsidActive ? (
                <>
                  <CheckCircle2 className="w-3.5 h-3.5 text-zatca-400 animate-pulse" />
                  <span>CSID Active ({onboardingStatus?.environment || 'Simulation'})</span>
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

        {/* Tab Navigation */}
        <div className="flex space-x-1 overflow-x-auto pb-1 scrollbar-none border-t border-slate-800/60 pt-1">
          {tabs.map((tab) => {
            const Icon = tab.icon;
            const isActive = activeTab === tab.id;
            return (
              <button
                key={tab.id}
                onClick={() => onTabChange(tab.id)}
                className={`flex items-center space-x-2 px-4 py-2.5 text-sm font-medium rounded-lg transition-all whitespace-nowrap ${
                  isActive
                    ? 'bg-zatca-600 text-white shadow-md shadow-zatca-900/30 font-semibold'
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

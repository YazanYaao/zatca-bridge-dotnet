import React, { useState } from 'react';
import { useMutation, useQuery, useQueryClient } from '@tanstack/react-query';
import { 
  Key, 
  Send, 
  Award, 
  CheckCircle, 
  Sparkles, 
  AlertTriangle, 
  ShieldCheck, 
  Loader2,
  Lock,
  ArrowRight
} from 'lucide-react';
import { api } from '../../api/client';
import { RequestComplianceCsidApiRequest } from '../../types/api';

const SANDBOX_PRESET: RequestComplianceCsidApiRequest = {
  otp: '123345',
  commonName: 'TST-886431145-310123456700003',
  organizationName: 'Test Company Ltd',
  organizationalUnitName: 'Riyadh Branch',
  vatRegistrationNumber: '310123456700003',
  serialNumber: '1-TST|2-TST|3-ed22f1d8-e6a2-1118-9b58-d9a8f11e445f',
  invoiceType: '1100',
  location: 'Riyadh',
  industry: 'Retail',
  countryCode: 'SA',
  environment: 'Simulation',
};

export const OnboardingTab: React.FC = () => {
  const queryClient = useQueryClient();
  const [formData, setFormData] = useState<RequestComplianceCsidApiRequest>(SANDBOX_PRESET);
  const [complianceResult, setComplianceResult] = useState<any>(null);
  const [productionResult, setProductionResult] = useState<any>(null);
  const [errorMessage, setErrorMessage] = useState<string | null>(null);

  // Status query
  const { data: status, isLoading: isStatusLoading } = useQuery({
    queryKey: ['onboardingStatus'],
    queryFn: api.getOnboardingStatus,
  });

  // Step 1: Request Compliance CSID
  const complianceMutation = useMutation({
    mutationFn: api.requestComplianceCsid,
    onSuccess: () => {
      setErrorMessage(null);
      queryClient.invalidateQueries({ queryKey: ['onboardingStatus'] });
    },
    onError: (err: any) => {
      setErrorMessage(err.detail || err.message || 'Failed to request Compliance CSID');
    },
  });

  // Step 2: Run Compliance Check
  const checkMutation = useMutation({
    mutationFn: api.submitComplianceCheck,
    onSuccess: (data) => {
      setErrorMessage(null);
      setComplianceResult(data);
    },
    onError: (err: any) => {
      setErrorMessage(err.detail || err.message || 'Compliance check failed');
    },
  });

  // Step 3: Promote to Production CSID
  const productionMutation = useMutation({
    mutationFn: (requestId?: string) => api.requestProductionCsid(requestId),
    onSuccess: (data) => {
      setErrorMessage(null);
      setProductionResult(data);
      queryClient.invalidateQueries({ queryKey: ['onboardingStatus'] });
    },
    onError: (err: any) => {
      setErrorMessage(err.detail || err.message || 'Failed to promote to Production CSID');
    },
  });

  const handleApplyPreset = () => {
    setFormData(SANDBOX_PRESET);
    setErrorMessage(null);
  };

  return (
    <div className="space-y-6">
      {/* Live CSID Banner */}
      <div className="bg-white border border-slate-200/90 rounded-2xl p-6 shadow-sm relative overflow-hidden">
        <div className="flex flex-col md:flex-row items-start md:items-center justify-between gap-4">
          <div className="flex items-center space-x-4">
            <div
              className={`w-12 h-12 rounded-xl flex items-center justify-center ${
                status?.isActive
                  ? 'bg-emerald-50 text-emerald-700 border border-emerald-200'
                  : 'bg-amber-50 text-amber-700 border border-amber-200'
              }`}
            >
              {isStatusLoading ? (
                <Loader2 className="w-6 h-6 animate-spin text-slate-400" />
              ) : status?.isActive ? (
                <ShieldCheck className="w-7 h-7 text-emerald-700" />
              ) : (
                <AlertTriangle className="w-7 h-7 text-amber-600" />
              )}
            </div>
            <div>
              <div className="flex items-center space-x-3">
                <h2 className="text-lg font-bold text-slate-900">Cryptographic Stamp Identifier (CSID)</h2>
                <span
                  className={`px-2.5 py-0.5 rounded-full text-xs font-semibold ${
                    status?.isActive
                      ? 'bg-emerald-50 text-emerald-800 border border-emerald-200'
                      : 'bg-amber-50 text-amber-800 border border-amber-200'
                  }`}
                >
                  {status?.isActive ? 'ACTIVE & ONBOARDED' : 'NOT ONBOARDED (UNSIGNED MODE)'}
                </span>
              </div>
              <p className="text-sm text-slate-500 mt-1">
                {status?.isActive
                  ? `Environment: ${status.environment || 'Simulation'} • Registered: ${status.createdAt ? new Date(status.createdAt).toLocaleString() : 'N/A'}`
                  : 'Operating in backward-compatible unsigned mode. Request a CSID to enable XAdES-BES digital signatures and 9-tag QR codes.'}
              </p>
            </div>
          </div>

          <button
            onClick={handleApplyPreset}
            className="flex items-center space-x-2 px-3.5 py-2 bg-slate-50 hover:bg-slate-100 text-slate-700 text-xs font-semibold rounded-xl transition border border-slate-200 shadow-sm"
          >
            <Sparkles className="w-4 h-4 text-emerald-700" />
            <span>Load Sandbox Presets</span>
          </button>
        </div>

        {/* Security Notice */}
        <div className="mt-4 pt-4 border-t border-slate-100 flex items-center space-x-2 text-xs text-slate-500">
          <Lock className="w-3.5 h-3.5 text-emerald-700 flex-shrink-0" />
          <span>
            Strict Enterprise Security: Private keys (<code className="text-slate-800 font-semibold font-mono">secp256k1</code>) and CSID credentials are vaulted securely in the database and never transmitted to the browser.
          </span>
        </div>
      </div>

      {/* Error alert */}
      {errorMessage && (
        <div className="bg-rose-50 border border-rose-200 text-rose-800 rounded-xl p-4 text-xs flex items-start space-x-3 shadow-sm">
          <AlertTriangle className="w-5 h-5 flex-shrink-0 text-rose-600 mt-0.5" />
          <div className="space-y-1">
            <p className="font-semibold text-rose-900">ZATCA Onboarding Error</p>
            <p className="text-rose-700">{errorMessage}</p>
          </div>
        </div>
      )}

      {/* 3-Step Guided Workflow */}
      <div className="grid grid-cols-1 lg:grid-cols-3 gap-6">
        {/* Step 1: Compliance CSID */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-5 flex flex-col justify-between shadow-sm">
          <div>
            <div className="flex items-center justify-between mb-4">
              <span className="w-7 h-7 rounded-lg bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs font-bold flex items-center justify-center font-mono">
                1
              </span>
              <span className="text-xs font-mono text-slate-400">POST /compliance</span>
            </div>
            <h3 className="font-bold text-slate-900 text-base flex items-center space-x-2">
              <Key className="w-4 h-4 text-emerald-700" />
              <span>Request Compliance CSID</span>
            </h3>
            <p className="text-xs text-slate-500 mt-1 mb-4">
              Generates an ECDSA keypair and ZATCA SAN CSR, and exchanges OTP for a compliance certificate.
            </p>

            <div className="space-y-3">
              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">One-Time Password (OTP)</label>
                <input
                  type="text"
                  value={formData.otp}
                  onChange={(e) => setFormData({ ...formData, otp: e.target.value })}
                  placeholder="e.g. 123345"
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-900 font-mono focus:bg-white focus:outline-none focus:border-emerald-600 focus:ring-2 focus:ring-emerald-600/20 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">Common Name (CN)</label>
                <input
                  type="text"
                  value={formData.commonName}
                  onChange={(e) => setFormData({ ...formData, commonName: e.target.value })}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-900 font-mono focus:bg-white focus:outline-none focus:border-emerald-600 focus:ring-2 focus:ring-emerald-600/20 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">VAT Registration Number (UID)</label>
                <input
                  type="text"
                  value={formData.vatRegistrationNumber || ''}
                  onChange={(e) => setFormData({ ...formData, vatRegistrationNumber: e.target.value })}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-900 font-mono focus:bg-white focus:outline-none focus:border-emerald-600 focus:ring-2 focus:ring-emerald-600/20 transition"
                />
              </div>

              <div>
                <label className="block text-xs font-medium text-slate-700 mb-1">Device Serial (SN)</label>
                <input
                  type="text"
                  value={formData.serialNumber || ''}
                  onChange={(e) => setFormData({ ...formData, serialNumber: e.target.value })}
                  className="w-full bg-slate-50 border border-slate-200 rounded-lg px-3 py-1.5 text-xs text-slate-900 font-mono focus:bg-white focus:outline-none focus:border-emerald-600 focus:ring-2 focus:ring-emerald-600/20 transition"
                />
              </div>
            </div>
          </div>

          <button
            onClick={() => complianceMutation.mutate(formData)}
            disabled={complianceMutation.isPending || !formData.otp}
            className="mt-6 w-full py-2.5 bg-emerald-700 hover:bg-emerald-800 disabled:bg-slate-100 disabled:text-slate-400 text-white font-semibold text-xs rounded-xl transition flex items-center justify-center space-x-2 shadow-sm"
          >
            {complianceMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Exchanging with ZATCA...</span>
              </>
            ) : (
              <>
                <span>Obtain Compliance CSID</span>
                <ArrowRight className="w-4 h-4" />
              </>
            )}
          </button>
        </div>

        {/* Step 2: Compliance Check */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-5 flex flex-col justify-between shadow-sm">
          <div>
            <div className="flex items-center justify-between mb-4">
              <span className="w-7 h-7 rounded-lg bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs font-bold flex items-center justify-center font-mono">
                2
              </span>
              <span className="text-xs font-mono text-slate-400">POST /compliance/invoices</span>
            </div>
            <h3 className="font-bold text-slate-900 text-base flex items-center space-x-2">
              <Send className="w-4 h-4 text-emerald-700" />
              <span>Run Compliance Invoice Check</span>
            </h3>
            <p className="text-xs text-slate-500 mt-1 mb-4">
              Signs a test B2B Tax Invoice with the active CSID and submits it to ZATCA's compliance validator.
            </p>

            {complianceResult ? (
              <div className="bg-slate-900 border border-slate-800 rounded-xl p-3 text-xs space-y-2">
                <div className="flex items-center justify-between">
                  <span className="font-semibold text-slate-300">Validation Status:</span>
                  <span
                    className={`px-2 py-0.5 rounded font-bold ${
                      complianceResult.reportingStatus === 'REPORTED' || complianceResult.clearanceStatus === 'CLEARED'
                        ? 'bg-emerald-950 text-emerald-300 border border-emerald-800'
                        : 'bg-amber-950 text-amber-300 border border-amber-800'
                    }`}
                  >
                    {complianceResult.reportingStatus || complianceResult.clearanceStatus || 'EVALUATED'}
                  </span>
                </div>
                <div className="text-slate-300 text-[11px] font-mono break-all max-h-36 overflow-y-auto">
                  {JSON.stringify(complianceResult, null, 2)}
                </div>
              </div>
            ) : (
              <div className="border border-dashed border-slate-200 bg-slate-50/50 rounded-xl p-6 text-center text-xs text-slate-500">
                Execute a compliance test to verify digital signature and invoice syntax with ZATCA Sandbox.
              </div>
            )}
          </div>

          <button
            onClick={() => checkMutation.mutate({})}
            disabled={checkMutation.isPending || !status?.isActive}
            className="mt-6 w-full py-2.5 bg-slate-900 hover:bg-slate-800 disabled:bg-slate-100 disabled:text-slate-400 text-white font-semibold text-xs rounded-xl transition flex items-center justify-center space-x-2 shadow-sm"
          >
            {checkMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Validating with ZATCA...</span>
              </>
            ) : (
              <>
                <CheckCircle className="w-4 h-4" />
                <span>Test Compliance Invoice</span>
              </>
            )}
          </button>
        </div>

        {/* Step 3: Production CSID Promotion */}
        <div className="bg-white border border-slate-200/90 rounded-2xl p-5 flex flex-col justify-between shadow-sm">
          <div>
            <div className="flex items-center justify-between mb-4">
              <span className="w-7 h-7 rounded-lg bg-emerald-50 border border-emerald-200 text-emerald-800 text-xs font-bold flex items-center justify-center font-mono">
                3
              </span>
              <span className="text-xs font-mono text-slate-400">POST /production/csids</span>
            </div>
            <h3 className="font-bold text-slate-900 text-base flex items-center space-x-2">
              <Award className="w-4 h-4 text-emerald-700" />
              <span>Promote to Production CSID</span>
            </h3>
            <p className="text-xs text-slate-500 mt-1 mb-4">
              Finalizes onboarding by exchanging the compliance CSID for an active Production CSID.
            </p>

            {productionResult ? (
              <div className="bg-slate-900 border border-emerald-800/80 rounded-xl p-3 text-xs space-y-2">
                <div className="flex items-center space-x-2 text-emerald-400 font-semibold">
                  <CheckCircle className="w-4 h-4" />
                  <span>Production CSID Granted!</span>
                </div>
                <div className="text-slate-300 text-[11px] font-mono break-all max-h-36 overflow-y-auto">
                  {JSON.stringify(productionResult, null, 2)}
                </div>
              </div>
            ) : (
              <div className="border border-dashed border-slate-200 bg-slate-50/50 rounded-xl p-6 text-center text-xs text-slate-500">
                After passing compliance validation, promote the certificate to activate full clearance & reporting.
              </div>
            )}
          </div>

          <button
            onClick={() => productionMutation.mutate(status?.requestId || undefined)}
            disabled={productionMutation.isPending || !status?.isActive}
            className="mt-6 w-full py-2.5 bg-emerald-700 hover:bg-emerald-800 disabled:bg-slate-100 disabled:text-slate-400 text-white font-semibold text-xs rounded-xl transition flex items-center justify-center space-x-2 shadow-sm"
          >
            {productionMutation.isPending ? (
              <>
                <Loader2 className="w-4 h-4 animate-spin" />
                <span>Promoting Certificate...</span>
              </>
            ) : (
              <>
                <Award className="w-4 h-4" />
                <span>Request Production CSID</span>
              </>
            )}
          </button>
        </div>
      </div>
    </div>
  );
};

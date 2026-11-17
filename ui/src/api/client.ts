import {
  ChainHistoryResponse,
  ComplianceCheckApiRequest,
  InvoiceDetailResponse,
  IssueInvoiceRequest,
  IssueInvoiceResult,
  OnboardingStatusResponse,
  OutboxListResponse,
  PaginatedInvoicesResponse,
  RequestComplianceCsidApiRequest,
} from '../types/api';

const BASE_URL = '/api/v1';

export class ApiError extends Error {
  status: number;
  title?: string;
  detail?: string;

  constructor(status: number, title?: string, detail?: string) {
    super(detail || title || `API Error HTTP ${status}`);
    this.name = 'ApiError';
    this.status = status;
    this.title = title;
    this.detail = detail;
  }
}

async function request<T>(endpoint: string, options?: RequestInit): Promise<T> {
  const url = `${BASE_URL}${endpoint}`;
  const response = await fetch(url, {
    headers: {
      'Content-Type': 'application/json',
      Accept: 'application/json',
      ...options?.headers,
    },
    ...options,
  });

  if (!response.ok) {
    let errorJson: any = null;
    try {
      errorJson = await response.json();
    } catch {
      // not JSON
    }

    const title = errorJson?.title || errorJson?.error || response.statusText;
    const detail = errorJson?.detail || errorJson?.message || 'An error occurred while processing the request.';
    throw new ApiError(response.status, title, detail);
  }

  // Handle empty 204 or non-JSON responses
  if (response.status === 204) {
    return {} as T;
  }

  return response.json();
}

export const api = {
  // Onboarding endpoints
  getOnboardingStatus: () => request<OnboardingStatusResponse>('/onboarding/status'),
  requestComplianceCsid: (data: RequestComplianceCsidApiRequest) =>
    request<any>('/onboarding/compliance-csid', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  submitComplianceCheck: (data: ComplianceCheckApiRequest) =>
    request<any>('/onboarding/compliance-check', {
      method: 'POST',
      body: JSON.stringify(data),
    }),
  requestProductionCsid: (complianceRequestId?: string) =>
    request<any>('/onboarding/production-csid', {
      method: 'POST',
      body: JSON.stringify({ complianceRequestId }),
    }),

  // Invoice endpoints
  getInvoices: (page: number = 1, pageSize: number = 20, sort: 'asc' | 'desc' = 'desc') =>
    request<PaginatedInvoicesResponse>(`/invoices?page=${page}&pageSize=${pageSize}&sort=${sort}`),
  getInvoiceById: (id: string) => request<InvoiceDetailResponse>(`/invoices/${encodeURIComponent(id)}`),
  getChainHistory: () => request<ChainHistoryResponse>('/invoices/chain'),
  issueInvoice: (data: IssueInvoiceRequest) =>
    request<IssueInvoiceResult>('/invoices', {
      method: 'POST',
      body: JSON.stringify(data),
    }),

  // Outbox endpoints
  getOutbox: (status?: number, limit: number = 50) => {
    const query = new URLSearchParams();
    if (status !== undefined) query.set('status', status.toString());
    query.set('limit', limit.toString());
    return request<OutboxListResponse>(`/outbox?${query.toString()}`);
  },
  processOutbox: (batchSize: number = 10) =>
    request<{ processedCount: number }>(`/outbox/process?batchSize=${batchSize}`, {
      method: 'POST',
    }),
};

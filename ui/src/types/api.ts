export type InvoiceTypeCode = 388 | 381 | 383 | 386;
export type InvoiceTransactionType = 0 | 1; // 0 = B2B, 1 = B2C
export type TaxCategoryCode = 'Standard' | 'ZeroRated' | 'Exempt' | 'OutOfScope';

export interface PartyDto {
  name: string;
  vatRegistrationNumber: string;
  street: string;
  buildingNumber: string;
  postalZone: string;
  city: string;
  district: string;
  countryCode?: string;
}

export interface TaxCategoryDto {
  code: TaxCategoryCode;
  rate?: number | null;
  exemptionReasonCode?: string | null;
  exemptionReason?: string | null;
}

export interface InvoiceLineItemDto {
  name: string;
  quantity: number;
  unitPrice: number;
  discountAmount: number;
  taxCategory: TaxCategoryDto;
}

export interface IssueInvoiceRequest {
  invoiceNumber: string;
  issueDate: string;
  issueTime: string;
  invoiceType: InvoiceTypeCode;
  transactionType: InvoiceTransactionType;
  seller: PartyDto;
  buyer?: PartyDto | null;
  lineItems: InvoiceLineItemDto[];
  allowanceTotal?: number;
  chargeTotal?: number;
  prepaidAmount?: number;
  payableRoundingAmount?: number;
}

export interface IssueInvoiceResult {
  invoiceId: string;
  invoiceNumber: string;
  invoiceCounterValue: number;
  previousInvoiceHash: string;
  invoiceHash: string;
  qrCodeBase64: string;
  ublXml: string;
  isSigned: boolean;
  issuedAt: string;
}

export interface InvoiceSummaryDto {
  id: string;
  uuid: string;
  invoiceNumber: string;
  issueDate: string;
  issueTime: string;
  invoiceTypeCode: number;
  transactionType: number;
  invoiceType: string;
  transactionTypeName: string;
  icv: number;
  lineExtensionAmount: number;
  totalTaxAmount: number;
  payableAmount: number;
  sellerName: string;
  sellerVatNumber: string;
  buyerName?: string | null;
  buyerVatNumber?: string | null;
  createdAt: string;
}

export interface PaginatedInvoicesResponse {
  totalCount: number;
  page: number;
  pageSize: number;
  items: InvoiceSummaryDto[];
}

export interface InvoiceDetailResponse {
  id: string;
  uuid: string;
  invoiceNumber: string;
  issueDate: string;
  issueTime: string;
  invoiceTypeCode: number;
  transactionType: number;
  invoiceType: string;
  transactionTypeName: string;
  icv: number;
  previousInvoiceHash?: string | null;
  invoiceHash: string;
  lineExtensionAmount: number;
  taxExclusiveAmount: number;
  taxInclusiveAmount: number;
  totalTaxAmount: number;
  payableAmount: number;
  sellerName: string;
  sellerVatNumber: string;
  buyerName?: string | null;
  buyerVatNumber?: string | null;
  xmlPayload: string;
  qrCodeBase64?: string | null;
  outboxStatus: string;
  createdAt: string;
}

export interface OutboxMessageItemDto {
  id: string;
  invoiceId: string;
  type: number;
  typeName: string;
  invoiceHash: string;
  invoiceCounterValue: number;
  status: number;
  statusName: string;
  retryCount: number;
  errorMessage?: string | null;
  createdAt: string;
  processedAt?: string | null;
}

export interface OutboxListResponse {
  totalCount: number;
  items: OutboxMessageItemDto[];
}

export interface ChainRecordItemDto {
  icv: number;
  previousHash: string;
  invoiceHash: string;
  createdAt: string;
}

export interface ChainHistoryResponse {
  items: ChainRecordItemDto[];
}

export interface OnboardingStatusResponse {
  isActive: boolean;
  id?: string | null;
  environment?: string | null;
  requestId?: string | null;
  createdAt?: string | null;
  message?: string | null;
}

export interface RequestComplianceCsidApiRequest {
  otp: string;
  commonName: string;
  organizationName?: string;
  organizationUnit?: string;
  organizationalUnitName?: string;
  vatRegistrationNumber?: string;
  serialNumber?: string;
  invoiceType?: string;
  location?: string;
  industry?: string;
  countryCode?: string;
  environment?: 'Simulation' | 'Sandbox' | 'Production';
}

export interface ComplianceCheckApiRequest {
  invoiceNumber?: string;
  seller?: PartyDto;
  buyer?: PartyDto;
}

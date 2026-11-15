# Product Requirements Document (PRD)
## ZatcaBridge: ZATCA Phase 2 E-Invoicing Middleware (.NET 8/9)

---

## 1. Executive Summary & Objective

**ZatcaBridge** is a high-performance, developer-friendly .NET 8/9 middleware and clean-architecture library designed to integrate Saudi Arabian ERPs, POS systems, and billing platforms with the Zakat, Tax and Customs Authority (**ZATCA**) Phase 2 (Fatoora) e-invoicing platform.

The middleware guarantees full compliance with:
- **ZATCA Phase 2 E-Invoicing Mandate** (UBL 2.1 XML, Cryptographic Hash Chaining, ECDSA/TLV QR Codes).
- **Zero-Drift Monetary Precision** (Strict `decimal` arithmetic with `MidpointRounding.AwayFromZero`).
- **Resilient Message Dispatch** (Transactional Outbox Pattern for B2B Clearance and B2C 24-hour Reporting).

---

## 2. Core Domain Architecture & Precision Rules

### 2.1 Mandatory Monetary Calculation Rules
Financial inaccuracies violate ZATCA validation rules and trigger rejection codes.
- **Rule:** Under no circumstances are `float` or `double` data types permitted for financial amounts, tax rates, or quantities.
- **Standard Rounding:** All money amounts must be rounded using `decimal.Round(value, 2, MidpointRounding.AwayFromZero)` for line totals and invoice totals.
- **Unit Price Precision:** Unit prices support up to 4 decimal places before line extension arithmetic.

### 2.2 Domain Entities & Value Objects

```
┌─────────────────────────────────────────────────────────────┐
│                     Invoice (Aggregate Root)                │
├─────────────────────────────────────────────────────────────┤
│ - Id: Guid                                                  │
│ - Uuid: Guid                                                │
│ - InvoiceNumber: string (e.g., INV-2026-0001)               │
│ - IssueDate: DateOnly                                       │
│ - IssueTime: TimeOnly                                       │
│ - InvoiceType: InvoiceTypeCode (388: Tax, 381: Credit, etc.)│
│ - TransactionType: InvoiceTransactionType (B2B, B2C)        │
│ - InvoiceCounterValue (ICV): long                           │
│ - PreviousInvoiceHash (PIH): string (Base64 SHA-256)        │
│ - Seller: Party                                             │
│ - Buyer: Party?                                             │
│ - LineItems: IReadOnlyList<InvoiceLineItem>                 │
│ - TaxTotals: IReadOnlyList<TaxSubtotal>                     │
│ - MonetaryTotal: LegalMonetaryTotal                         │
│ - CryptographicStamp: CryptographicStampInfo?               │
└─────────────────────────────────────────────────────────────┘
                               │ 1..*
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                    InvoiceLineItem (Entity)                 │
├─────────────────────────────────────────────────────────────┤
│ - Id: Guid                                                  │
│ - Name: string                                              │
│ - Quantity: decimal                                         │
│ - UnitPrice: decimal                                        │
│ - DiscountAmount: decimal                                   │
│ - LineExtensionAmount: decimal (Qty * Price - Discount)     │
│ - TaxCategory: TaxCategory                                  │
│ - TaxAmount: decimal (LineExtensionAmount * Rate)           │
│ - TotalAmount: decimal (LineExtensionAmount + TaxAmount)    │
└─────────────────────────────────────────────────────────────┘
                               │
                               ▼
┌─────────────────────────────────────────────────────────────┐
│                    TaxCategory (Value Object)               │
├─────────────────────────────────────────────────────────────┤
│ - Code: TaxCategoryCode (S=Standard, Z=Zero, E=Exempt, O=Out)│
│ - Rate: decimal (e.g. 0.15m for S, 0.00m for Z/E/O)         │
│ - ExemptionReasonCode: string?                              │
│ - ExemptionReason: string?                                  │
└─────────────────────────────────────────────────────────────┘
```

### 2.3 Mathematical Invariants
1. **Line Extension Amount:**
   $$\text{LineExtensionAmount} = \text{round}((\text{Quantity} \times \text{UnitPrice}) - \text{DiscountAmount}, 2)$$
2. **Line Tax Amount:**
   $$\text{TaxAmount} = \text{round}(\text{LineExtensionAmount} \times \text{TaxRate}, 2)$$
3. **Line Gross Total:**
   $$\text{TotalAmount} = \text{LineExtensionAmount} + \text{TaxAmount}$$
4. **Tax Exclusive Amount:**
   $$\text{TaxExclusiveAmount} = \sum \text{LineExtensionAmount} - \text{InvoiceDiscounts} + \text{InvoiceCharges}$$
5. **Total Tax Amount:**
   $$\text{TotalTaxAmount} = \sum \text{TaxAmount}$$
6. **Payable Amount (Gross Total):**
   $$\text{PayableAmount} = \text{TaxExclusiveAmount} + \text{TotalTaxAmount}$$

---

## 3. ZATCA Cryptographic Engine

### 3.1 TLV Base64 QR Code Encoding
ZATCA requires QR codes formatted as binary Tag-Length-Value (TLV) and converted to a Base64 string.
- **TLV Structure:**
  - `Tag`: 1 byte representing the tag number (1 to 9).
  - `Length`: 1 byte representing length $L$ of the value in bytes (UTF-8 length).
  - `Value`: $L$ bytes containing UTF-8 or raw binary data.

#### Tag Definitions:
| Tag | Field Name | Format / Encoding | Phase Applicability |
|:---:|:---|:---|:---|
| **1** | Seller Name | UTF-8 String | Phase 1 & 2 |
| **2** | VAT Registration Number | 15-digit Numeric String (UTF-8) | Phase 1 & 2 |
| **3** | Invoice Timestamp | ISO 8601 UTC String (`yyyy-MM-ddTHH:mm:ssZ`) | Phase 1 & 2 |
| **4** | Invoice Total (with VAT) | Decimal formatted string (`0.00`) | Phase 1 & 2 |
| **5** | Total VAT | Decimal formatted string (`0.00`) | Phase 1 & 2 |
| **6** | Invoice SHA-256 Hash | Raw 32 bytes binary digest or Base64 | Phase 2 B2C |
| **7** | ECDSA Digital Signature | Binary signature bytes | Phase 2 B2C |
| **8** | ECDSA Public Key / Cert | Binary X.509 public key / certificate | Phase 2 B2C |
| **9** | Stamp Signature | ZATCA Technical Stamp Signature | Phase 2 B2C |

### 3.2 Invoice Hashing & Hash Chaining
1. **SHA-256 Invoice Hash:**
   - Canonicalized UBL 2.1 XML (excluding signature nodes, QR element, and UBL extensions) is hashed using SHA-256.
   - The output is represented as a Base64-encoded string.
2. **Invoice Counter Value (`ICV`):**
   - Strictly sequential integer starting at `1` for the first invoice generated by a specific Cryptographic Stamp Identifier (CSID).
   - Invariant: $\text{ICV}_{n} = \text{ICV}_{n-1} + 1$.
3. **Previous Invoice Hash (`PIH`):**
   - Each invoice embeds the SHA-256 hash of its predecessor in the chain.
   - **Genesis Invoice:** For the very first invoice ($\text{ICV} = 1$), the previous hash is the standard ZATCA initial hash:
     `NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI4NjliMDExMzg0NmNmNmIx` (Base64 encoding of SHA-256 of ASCII `"0"`).

---

## 4. UBL 2.1 XML Generator & Clearance/Reporting Workflows

### 4.1 UBL 2.1 Standards
The system serializes invoices to ZATCA-compliant UBL 2.1 XML syntax (`<Invoice>` root) adhering to:
- Standard namespaces: `urn:oasis:names:specification:ubl:schema:xsd:Invoice-2`, `cac`, `cbc`, `ext`, `sig`.
- Profile ID: `reporting:1.0` (Simplified / B2C) or `clearance:1.0` (Standard / B2B).
- Mandatory invoice segments: Supplier party, Customer party, Delivery details, Payment means, Allowance/Charge, Tax totals by category, Legal monetary totals, and Line items.

### 4.2 Outbox Pattern & Resilient Integration
To prevent transaction failures and guarantee delivery:

```
POS / ERP Transaction
        │
        ▼
┌───────────────────────────────────────────────┐
│ Database Transaction                          │
│  ├── Save Invoice (Status: Pending)           │
│  └── Insert Outbox Message (Id, Payload, ...) │
└───────────────────────────────────────────────┘
        │
        ▼
┌───────────────────────────────────────────────┐
│ Outbox Processor (Background Worker)          │
│  ├── Reads Unprocessed Messages               │
│  ├── B2B: Calls ZATCA Clearance API (Sync)    │
│  │    ├── If Cleared: Stamp XML, Mark Issued  │
│  │    └── If Rejected: Mark Failed with Codes │
│  └── B2C: Calls ZATCA Reporting API (Async)   │
│       ├── Immediate Issue with QR Code        │
│       └── Report within 24 hours window       │
│  └── Marks Outbox Message Completed           │
└───────────────────────────────────────────────┘
```

1. **B2B Clearance (Standard Invoices):**
   - Must be cleared in real-time by ZATCA before being provided to the buyer.
   - Marked `Cleared` only upon receiving cryptographic clearance response from ZATCA API.
2. **B2C Reporting (Simplified Invoices):**
   - Issued to the consumer immediately at checkout with TLV QR code printed on receipt.
   - Reported asynchronously to ZATCA via the Reporting API within the statutory 24-hour window.
3. **Idempotency & Retry:**
   - All network requests use exponential backoff with jitter and idempotent UUID keys.

---

## 5. Non-Functional Requirements & Security
- **Performance:** Cryptographic hashing and TLV encoding must complete in $< 5\,\text{ms}$ per invoice.
- **Security:** Private keys (ECDSA secp256k1) and CSID certificates must be stored securely (encrypted at rest / HSM / Azure KeyVault).
- **Auditability:** Complete immutable audit log of hash chains and XML payloads.

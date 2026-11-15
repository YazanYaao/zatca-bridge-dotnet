# Milestone 1: Domain & Cryptographic Core
## Implementation Plan & TDD Slices

---

## 1. Overview & Execution Rules

This document outlines the 4 vertical-slice TDD tasks for **Milestone 1** of ZatcaBridge.
Every task strictly adheres to the TDD loop defined in `AGENTS.md` and `.agents/skills/engineering/tdd/`:
1. **Red**: Write the failing xUnit unit test first.
2. **Green**: Write minimal Domain code to pass the test.
3. **Verify**: Run `dotnet test` ensuring 0 failures.
4. **Commit**: Git commit with atomic message convention.

### Core Architectural Guardrails
- **Monetary Precision**: All financial calculations must strictly use `decimal` with `MidpointRounding.AwayFromZero` to 2 decimal places. No `float` or `double`.
- **Pure Domain**: Domain classes in `src/ZatcaBridge.Domain` must not depend on external frameworks or infrastructure layers.

---

## 2. Vertical Slice Tasks

### Task 1.1: Monetary Precision & Line Item Calculations
- **Goal**: Implement `TaxCategory` value object and `InvoiceLineItem` entity with strictly rounded calculations for line extensions, discounts, and VAT amounts.
- **Blocked By**: None (can start immediately).
- **Test File**: `tests/ZatcaBridge.UnitTests/Domain/InvoiceLineItemTests.cs`
- **Domain Classes**:
  - `src/ZatcaBridge.Domain/Enums/TaxCategoryCode.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/TaxCategory.cs`
  - `src/ZatcaBridge.Domain/Entities/InvoiceLineItem.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] `TaxCategory` properly instantiates Standard (15%), Zero-rated (0%), Exempt (0%), and Out-of-Scope (0%).
  - [ ] `InvoiceLineItem` calculates `LineExtensionAmount = decimal.Round((Quantity * UnitPrice) - DiscountAmount, 2, MidpointRounding.AwayFromZero)`.
  - [ ] Half-cent rounding edge case: `UnitPrice = 10.035m`, `Quantity = 1` rounds to `10.04m` under `MidpointRounding.AwayFromZero`.
  - [ ] Calculates `TaxAmount = decimal.Round(LineExtensionAmount * TaxCategory.Rate, 2, MidpointRounding.AwayFromZero)`.
  - [ ] Calculates `TotalAmount = LineExtensionAmount + TaxAmount`.
  - [ ] Rejects negative unit price, negative quantity, or discount exceeding gross amount.

---

### Task 1.2: Invoice Aggregate Totals & Invariants
- **Goal**: Implement the `Invoice` aggregate root, `Party` value object, and `LegalMonetaryTotal` computation across multiple line items.
- **Blocked By**: Task 1.1
- **Test File**: `tests/ZatcaBridge.UnitTests/Domain/InvoiceAggregateTests.cs`
- **Domain Classes**:
  - `src/ZatcaBridge.Domain/Enums/InvoiceTypeCode.cs`
  - `src/ZatcaBridge.Domain/Enums/InvoiceTransactionType.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/Party.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/TaxSubtotal.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/LegalMonetaryTotal.cs`
  - `src/ZatcaBridge.Domain/Entities/Invoice.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] `Invoice` aggregate aggregates multiple line items with different tax categories (e.g. Standard 15% and Zero-rated 0%).
  - [ ] Computes `TaxSubtotal` grouping by `TaxCategory`.
  - [ ] Computes `TaxExclusiveAmount = Sum(LineExtensionAmount)`.
  - [ ] Computes `TaxInclusiveAmount = TaxExclusiveAmount + Sum(TaxAmount)`.
  - [ ] Computes `PayableAmount = TaxInclusiveAmount - PrepaidAmount + RoundingAmount`.
  - [ ] Validates VAT registration number format for Saudi entities (15 digits starting and ending with 3).
  - [ ] Enforces non-empty line items and valid issue date/time.

---

### Task 1.3: ZATCA TLV Base64 QR Code Encoder
- **Goal**: Implement binary Tag-Length-Value (TLV) encoding and Base64 conversion for ZATCA Phase 1 & 2 QR specifications.
- **Blocked By**: Task 1.1, Task 1.2
- **Test File**: `tests/ZatcaBridge.UnitTests/Domain/TlvEncoderTests.cs`
- **Domain Classes**:
  - `src/ZatcaBridge.Domain/Cryptography/TlvTag.cs`
  - `src/ZatcaBridge.Domain/Cryptography/TlvEncoder.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/ZatcaQrCodeData.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] TLV encoding converts Tag (1 byte) + Length (1 byte) + UTF-8 string value into correct byte sequence.
  - [ ] Encodes Tags 1 to 5:
    - Tag 1: Seller's Name
    - Tag 2: VAT Registration Number
    - Tag 3: Time Stamp (ISO 8601 UTC)
    - Tag 4: Invoice Total (with VAT) formatted as `0.00`
    - Tag 5: Total VAT formatted as `0.00`
  - [ ] Produces verified Base64 output matching ZATCA sample test vectors.
  - [ ] Supports Tags 6 to 9 binary stub payloads for Phase 2 B2C simplified invoices (SHA-256 hash, ECDSA signature, public key, stamp).
  - [ ] Handles multi-byte Arabic UTF-8 characters without byte truncation or miscalculated length.

---

### Task 1.4: SHA-256 Invoice Hashing & Hash Chaining (`ICV` & `PIH`)
- **Goal**: Implement canonical invoice SHA-256 digest calculation, `InvoiceCounterValue` (ICV) sequential tracking, and `PreviousInvoiceHash` (PIH) chain link validation.
- **Blocked By**: Task 1.2
- **Test File**: `tests/ZatcaBridge.UnitTests/Domain/InvoiceHashChainTests.cs`
- **Domain Classes**:
  - `src/ZatcaBridge.Domain/Cryptography/InvoiceHashCalculator.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/InvoiceCounterValue.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/PreviousInvoiceHash.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/HashChainRecord.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Computes standard SHA-256 digest returning 32-byte Base64-encoded string.
  - [ ] Genesis invoice ($ICV = 1$) verifies against the official ZATCA initial hash constant:
    `NWZlY2ViNjZmZmM4NmYzOGQ5NTI3ODZjNmQ2OTZjNzljMmRiYzIzOWRkNGU5MWI4NjliMDExMzg0NmNmNmIx`.
  - [ ] Sequential counter invariant: $ICV_n = ICV_{n-1} + 1$, rejecting non-consecutive or decreasing counters.
  - [ ] Chain linkage invariant: Invoice $n$'s `PreviousInvoiceHash` must equal the computed hash of Invoice $n-1$.
  - [ ] Detects tampering or break in the cryptographic hash chain.

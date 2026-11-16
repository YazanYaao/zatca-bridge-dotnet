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


---

## 3. Milestone 2: UBL 2.1 XML Generation, Application Orchestration, & Transactional Outbox Persistence

### Task 2.1: UBL 2.1 XML Invoice Serializer (`UblXmlGenerator`)
- **Goal**: Serialize `Invoice` aggregates into standard ZATCA-compliant UBL 2.1 XML syntax (`<Invoice>`) including required namespaces, profile IDs, embedded document references (ICV, PIH, QR), tax subtotals, legal monetary totals, and line items.
- **Blocked By**: Milestone 1 (Completed).
- **Test File**: `tests/ZatcaBridge.UnitTests/Application/UblXmlGeneratorTests.cs`
- **Application Contracts & Classes**:
  - `src/ZatcaBridge.Application/Common/Interfaces/IZatcaXmlGenerator.cs`
  - `src/ZatcaBridge.Application/Xml/UblXmlGenerator.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Generates well-formed XML with standard UBL 2.1 namespaces (`urn:oasis:names:specification:ubl:schema:xsd:Invoice-2`, `cac`, `cbc`, `ext`, `sig`).
  - [ ] Sets `cbc:ProfileID` to `reporting:1.0` for B2C simplified invoices and `clearance:1.0` for B2B standard invoices.
  - [ ] Embeds `AdditionalDocumentReference` nodes for `ICV`, `PIH`, and `QR` (Base64 TLV data).
  - [ ] Renders `cac:AccountingSupplierParty` and `cac:AccountingCustomerParty` (when present) with KSA VAT and postal details.
  - [ ] Renders `cac:TaxTotal` with `currencyID="SAR"` and individual `cac:TaxSubtotal` elements grouped by `TaxCategory`.
  - [ ] Renders `cac:LegalMonetaryTotal` with `currencyID="SAR"` formatted to 2 decimal places with `MidpointRounding.AwayFromZero`.
  - [ ] Renders `cac:InvoiceLine` nodes with line extension amounts, tax subtotals, and item names.
  - [ ] Handles Arabic UTF-8 text cleanly without byte-order-mark (BOM) corruption.

---

### Task 2.2: Application Layer Contracts & Invoice Issuance Use Case
- **Goal**: Implement application orchestration for issuing an invoice via CQRS command and handler (`IssueInvoiceCommand` & `IssueInvoiceHandler`).
- **Blocked By**: Task 2.1
- **Test File**: `tests/ZatcaBridge.UnitTests/Application/IssueInvoiceHandlerTests.cs`
- **Application Classes & Contracts**:
  - `src/ZatcaBridge.Application/Invoices/Commands/IssueInvoiceCommand.cs`
  - `src/ZatcaBridge.Application/Invoices/Commands/IssueInvoiceResult.cs`
  - `src/ZatcaBridge.Application/Invoices/Commands/IssueInvoiceHandler.cs`
  - `src/ZatcaBridge.Application/Common/Interfaces/IHashChainRepository.cs`
  - `src/ZatcaBridge.Application/Common/Interfaces/IInvoiceOutboxRepository.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Validates command parameters and constructs `Invoice` domain aggregate.
  - [ ] Retrieves preceding hash chain record from `IHashChainRepository` or initiates genesis hash.
  - [ ] Computes SHA-256 invoice hash and generates TLV Base64 QR code.
  - [ ] Produces signed/compliant UBL 2.1 XML string via `IZatcaXmlGenerator`.
  - [ ] Atomically stages invoice and transactional outbox message in `IInvoiceOutboxRepository`.

---

### Task 2.3: EF Core Persistence & Transactional Outbox
- **Goal**: Implement database entities, EF Core configurations, and repositories in `ZatcaBridge.Infrastructure`.
- **Blocked By**: Task 2.2
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/OutboxRepositoryTests.cs`
- **Infrastructure Classes**:
  - `src/ZatcaBridge.Infrastructure/Persistence/ZatcaDbContext.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Entities/InvoiceEntity.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Entities/OutboxMessageEntity.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Repositories/InvoiceOutboxRepository.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Repositories/HashChainRepository.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Configures EF Core models with strict monetary precision `decimal(18,2)`.
  - [ ] Persists invoice aggregate state and outbox messages within a single atomic database transaction.
  - [ ] Supports outbox message states (`Pending`, `Processing`, `Completed`, `Failed`) and retry counters.

---

### Task 2.4: Outbox Processor & ZATCA Reporting/Clearance State Machine
- **Goal**: Implement background worker service and state machine for B2B real-time clearance and B2C asynchronous 24-hour reporting.
- **Blocked By**: Task 2.3
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/OutboxProcessorTests.cs`
- **Infrastructure Classes**:
  - `src/ZatcaBridge.Infrastructure/BackgroundServices/OutboxProcessor.cs`
  - `src/ZatcaBridge.Infrastructure/Services/ZatcaApiReportingClient.cs`
  - `src/ZatcaBridge.Infrastructure/Services/ZatcaApiClearanceClient.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Polls and locks unprocessed outbox messages to avoid duplicate processing.
  - [ ] B2B invoices dispatched to clearance endpoint; transitions status to `Cleared` or `Rejected`.
  - [ ] B2C invoices dispatched to reporting endpoint within 24 hours; transitions status to `Reported`.
  - [ ] Implements exponential backoff with jitter on transient network errors.

---

## 4. Milestone 3: ZATCA HTTP Gateway Adapter, Background Outbox Worker, & ASP.NET Core Minimal APIs

### Task 3.1: ZATCA Sandbox/Simulation HTTP Gateway Client (`ZatcaHttpGatewayClient`)
- **Goal**: Implement `ZatcaHttpGatewayClient` in `ZatcaBridge.Infrastructure/Gateway/` implementing `IZatcaGatewayClient` over `HttpClient`, serializing `{ invoiceHash, uuid, invoice: base64Xml }` payload to `/invoices/clearance/single` and `/invoices/reporting/single` with required headers (`Accept-Version: V2`, `Accept-Language: en`), and mapping HTTP 200/202, 400 validation rejections, and 5xx/network errors into `ZatcaGatewayResponse`.
- **Blocked By**: Milestone 2 (Completed).
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/ZatcaHttpGatewayClientTests.cs`
- **Infrastructure Classes & Contracts**:
  - `src/ZatcaBridge.Infrastructure/Gateway/ZatcaGatewayOptions.cs`
  - `src/ZatcaBridge.Infrastructure/Gateway/ZatcaHttpGatewayClient.cs`
  - `src/ZatcaBridge.Application/Common/Interfaces/IZatcaGatewayClient.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Serializes request body as `{ invoiceHash, uuid, invoice }` where `invoice` is Base64-encoded UTF-8 UBL XML.
  - [ ] Appends required HTTP headers: `Accept-Version: V2` and `Accept-Language: en`.
  - [ ] Dispatches B2B invoices to `POST /invoices/clearance/single` and parses 200/202 response (`clearanceStatus: "CLEARED"`).
  - [ ] Dispatches B2C invoices to `POST /invoices/reporting/single` and parses 200/202 response (`reportingStatus: "REPORTED"`).
  - [ ] Maps 400 Bad Request / validation rejections to non-transient rejection (`IsSuccess = false, IsRejected = true, IsTransient = false`).
  - [ ] Maps 5xx errors (e.g., 503 Service Unavailable) and `HttpRequestException` to transient retryable failure (`IsSuccess = false, IsRejected = false, IsTransient = true`).

---

### Task 3.2: Background Outbox Worker & Dependency Injection Registration
- **Goal**: Implement `OutboxBackgroundWorker : BackgroundService` that periodically executes `OutboxProcessor.ProcessPendingMessagesAsync` on a configurable polling interval, and add DI extension methods `AddZatcaApplication` and `AddZatcaInfrastructure`.
- **Blocked By**: Task 3.1
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/OutboxBackgroundWorkerTests.cs`
- **Application & Infrastructure Classes**:
  - `src/ZatcaBridge.Infrastructure/BackgroundServices/OutboxBackgroundWorker.cs`
  - `src/ZatcaBridge.Application/DependencyInjection.cs`
  - `src/ZatcaBridge.Infrastructure/DependencyInjection.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Worker runs periodically in background and invokes `OutboxProcessor` within a scoped `IServiceScope`.
  - [ ] Gracefully handles `OperationCanceledException` during application shutdown.
  - [ ] `AddZatcaApplication` registers application use cases, validators, and handlers.
  - [ ] `AddZatcaInfrastructure` registers EF Core DbContext, repositories, `UblXmlGenerator`, `HttpClient`, and `ZatcaHttpGatewayClient`.

---

### Task 3.3: ASP.NET Core Minimal API Endpoints & RFC 7807 Error Handling
- **Goal**: Implement Minimal API route endpoints in `ZatcaBridge.Api` with RFC 7807 `ProblemDetails` exception handling.
- **Blocked By**: Task 3.2
- **Test File**: `tests/ZatcaBridge.UnitTests/Api/EndpointRouteTests.cs`
- **Api Classes & Middleware**:
  - `src/ZatcaBridge.Api/Endpoints/InvoiceEndpoints.cs`
  - `src/ZatcaBridge.Api/Middleware/ProblemDetailsExceptionHandler.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] `POST /api/v1/invoices` validates input DTO, issues invoice via `IssueInvoiceHandler`, and returns HTTP 201 Created with invoice hash, ICV, QR code, and UBL XML.
  - [ ] `GET /api/v1/invoices/chain/latest` returns HTTP 200 OK with latest ICV and hash or HTTP 404 Not Found if empty.
  - [ ] `POST /api/v1/outbox/process` manually triggers immediate outbox batch processing and returns count of processed messages.
  - [ ] Domain exceptions (`ValidationException`, `InvalidOperationException`) map to RFC 7807 `ProblemDetails` with HTTP 400 Bad Request.

---

### Task 3.4: End-to-End API Integration Tests
- **Goal**: Full vertical-slice integration test suite utilizing `WebApplicationFactory<Program>` with an in-memory SQLite database and mocked HTTP gateway.
- **Blocked By**: Task 3.3
- **Test File**: `tests/ZatcaBridge.UnitTests/Api/InvoiceIntegrationTests.cs`
- **Integration Test Infrastructure**:
  - `src/ZatcaBridge.Api/Program.cs`
- **Acceptance Criteria & Test Cases**:
  - [ ] Issues Genesis invoice via API endpoint, verifies database persistence and outbox message creation.
  - [ ] Issues second chained invoice, verifying sequential ICV (2) and PIH matching invoice 1's hash.
  - [ ] Triggers outbox processing and verifies status transitions to `Cleared` or `Reported`.
  - [ ] Confirms full RFC 7807 problem details response on invalid VAT number or malformed line items.

---

## 5. Milestone 4: ZATCA Phase 2 Cryptographic Onboarding, XAdES-BES XML Signing, QR TLV Tags 1–9, & Live Sandbox Integration

### Task 4.1: ECDSA secp256k1 Key Generation, ZATCA CSR Generator, & CSID Domain Model
- **Goal**: Implement `ZatcaCsrGenerator` using `BouncyCastle.Cryptography` generating an ECDSA `secp256k1` key pair and PKCS#10 Certificate Signing Request (CSR) with ZATCA mandatory Subject DN attributes (CN, OU, O, C) and custom extension OID `1.3.6.1.4.1.311.20.2` (`CertificateTemplateName = PREZATCA-Code-Signing` for Simulation/Sandbox or `ZATCA-Code-Signing` for Production), along with the `CsidCredential` domain model.
- **Blocked By**: Milestone 3 (Completed).
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/ZatcaCsrGeneratorTests.cs`
- **Domain & Infrastructure Classes**:
  - `src/ZatcaBridge.Domain/Cryptography/CsidCredential.cs`
  - `src/ZatcaBridge.Domain/Cryptography/CsidEnvironment.cs`
  - `src/ZatcaBridge.Infrastructure/Cryptography/ZatcaCsrGenerator.cs`
  - `src/ZatcaBridge.Application/Common/Interfaces/IZatcaCsrGenerator.cs`
- **Acceptance Criteria & Test Cases**:
  - [x] Generates ECDSA key pair strictly using curve `secp256k1`.
  - [x] Exports private key as PKCS#8 PEM string.
  - [x] Constructs PKCS#10 CSR with standard Subject DN: `CN=<CommonName>`, `OU=<OrganizationUnit>`, `O=<OrganizationName>`, `C=SA`.
  - [x] Embeds custom ASN.1 Microsoft template extension OID `1.3.6.1.4.1.311.20.2` with `PREZATCA-Code-Signing` when targeting Simulation/Sandbox.
  - [x] Embeds `ZATCA-Code-Signing` when targeting Production.
  - [x] Exports valid PEM formatted CSR string starting with `-----BEGIN CERTIFICATE REQUEST-----`.

---

### Task 4.2: CSID Entity, EF Core Persistence, & Repository (`EfCsidRepository`)
- **Goal**: Implement `CsidCredentialEntity`, EF Core mapping in `ZatcaDbContext`, and `EfCsidRepository` implementing `ICsidRepository` in Application layer, providing query/update for the currently active CSID.
- **Blocked By**: Task 4.1
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/EfCsidRepositoryTests.cs`
- **Application & Infrastructure Classes**:
  - `src/ZatcaBridge.Application/Common/Interfaces/ICsidRepository.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Entities/CsidCredentialEntity.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/Repositories/EfCsidRepository.cs`
  - `src/ZatcaBridge.Infrastructure/Persistence/ZatcaDbContext.cs`
- **Acceptance Criteria & Test Cases**:
  - [x] Configures `CsidCredentials` table in EF Core SQLite schema.
  - [x] Stores private key PEM, X.509 certificate PEM, CSID secret, request ID, compliance/production status, and active indicator.
  - [x] Supports atomically setting an active CSID and deactivating older CSIDs.
  - [x] `GetActiveCsidAsync` returns the current active CSID or `null` if none is onboarded.

---

### Task 4.3: XAdES-BES XML Signer & QR Tags 1–9 Generator (`XadesXmlSigner`)
- **Goal**: Implement two-pass canonical XML signing producing `ext:UBLExtensions` with `ds:Signature`, `ds:SignedInfo`, `ds:SignatureValue`, `ds:KeyInfo` (X.509 cert), and `xades:QualifyingProperties`, and encode Phase 2 QR code with Tags 1 to 9 (Tag 6: Invoice Hash, Tag 7: Digital Signature, Tag 8: Public Key, Tag 9: Certificate Stamp Signature).
- **Blocked By**: Task 4.2
- **Test File**: `tests/ZatcaBridge.UnitTests/Infrastructure/XadesXmlSignerTests.cs`
- **Application & Infrastructure Classes**:
  - `src/ZatcaBridge.Application/Common/Interfaces/IXadesXmlSigner.cs`
  - `src/ZatcaBridge.Infrastructure/Cryptography/XadesXmlSigner.cs`
  - `src/ZatcaBridge.Domain/ValueObjects/ZatcaQrCodeData.cs`
- **Acceptance Criteria & Test Cases**:
  - [x] Calculates canonical C14N SHA-256 Invoice Hash on pre-signed XML.
  - [x] Computes ECDSA signature over the canonical hash using the `secp256k1` private key.
  - [x] Extracts public key and CA certificate signature from the CSID X.509 certificate.
  - [x] Produces Phase 2 QR code containing all Tags 1 through 9.
  - [x] Injects `ds:Signature` and XAdES-BES `SignedProperties` into `ext:UBLExtensions/ext:UBLExtension/ext:ExtensionContent`.
  - [x] Embeds completed Tags 1–9 QR code in `AdditionalDocumentReference` with `ID = QR`.

---

### Task 4.4: Dual-Mode Issuance Integration & Onboarding API Endpoints
- **Goal**: Wire `ICsidRepository` into `IssueInvoiceHandler` for automatic dual-mode operation (unsigned mode if no CSID vs signed XAdES-BES mode when CSID active), update `ZatcaHttpGatewayClient` for CSID Basic Auth (`BinarySecurityToken:Secret`), and expose onboarding endpoints (`POST /api/v1/onboarding/compliance-csid`, `POST /api/v1/onboarding/compliance-check`, `POST /api/v1/onboarding/production-csid`, `GET /api/v1/onboarding/status`).
- **Blocked By**: Task 4.3
- **Test File**: `tests/ZatcaBridge.UnitTests/Application/DualModeIssuanceTests.cs`, `tests/ZatcaBridge.UnitTests/Infrastructure/ZatcaOnboardingClientTests.cs`, and `tests/ZatcaBridge.UnitTests/Api/OnboardingEndpointsTests.cs`
- **Application, Infrastructure, & Api Classes**:
  - `src/ZatcaBridge.Application/Common/Interfaces/IZatcaOnboardingClient.cs`
  - `src/ZatcaBridge.Application/Invoices/Commands/IssueInvoiceHandler.cs`
  - `src/ZatcaBridge.Infrastructure/Gateway/ZatcaHttpGatewayClient.cs`
  - `src/ZatcaBridge.Infrastructure/Gateway/ZatcaOnboardingClient.cs`
  - `src/ZatcaBridge.Api/Endpoints/OnboardingEndpoints.cs`
- **Acceptance Criteria & Test Cases**:
  - [x] `IssueInvoiceHandler` seamlessly generates unsigned UBL (Tags 1–5 QR) if no CSID is active (preserving 100% of existing tests).
  - [x] `IssueInvoiceHandler` automatically switches to XAdES-BES signed UBL (Tags 1–9 QR) when an active CSID is present.
  - [x] `POST /api/v1/onboarding/compliance-csid` generates a new private key and CSR for a given tax entity, sends CSR and OTP to ZATCA compliance CSID endpoint, and saves the returned certificate/secret.
  - [x] `POST /api/v1/onboarding/compliance-check` signs and validates a test invoice against `/compliance/invoices`.
  - [x] `POST /api/v1/onboarding/production-csid` promotes compliance CSID to production CSID via `/production/csids`.
  - [x] `GET /api/v1/onboarding/status` returns non-sensitive metadata for the active CSID.
  - [x] `ZatcaHttpGatewayClient` automatically uses CSID Basic Authentication for all live sandbox clearance/reporting requests.

# ZatcaBridge - Agent Instructions & Architecture Guidelines

## Tech Stack
- **Framework:** .NET 8/9, C# 12
- **API Framework:** ASP.NET Core Minimal APIs
- **Testing:** xUnit

## Architecture
Strict Clean Architecture structure:
- **`Domain` (`src/ZatcaBridge.Domain`):** Core enterprise entities, value objects, domain exceptions, and business logic. Independent of any other layer or external framework.
- **`Application` (`src/ZatcaBridge.Application`):** Use cases, business workflows, commands/queries, mapping, and interfaces (ports). References `Domain`.
- **`Infrastructure` (`src/ZatcaBridge.Infrastructure`):** External services, database persistence, XML generation/serialization, cryptography, and network adapters. References `Application`.
- **`Api` (`src/ZatcaBridge.Api`):** Minimal API endpoints, middleware, dependency injection configuration, and HTTP request/response handling. References `Application` and `Infrastructure`.
- **`UnitTests` (`tests/ZatcaBridge.UnitTests`):** Comprehensive unit tests. References `Domain` and `Application`.

---

## Critical Rules

### CRITICAL RULE 1: Monetary Precision
- **Never** use `float` or `double` for currency, tax, or totals.
- Strictly use `decimal` with `MidpointRounding.AwayFromZero`.

### CRITICAL RULE 2: ZATCA Phase 2 Compliance
- Requires **UBL 2.1 XML** standard invoice format.
- Requires **SHA-256** hash chaining (`PreviousInvoiceHash`).
- Requires **TLV Base64** QR encoding for compliance and reporting.

### CRITICAL RULE 3: Test-Driven Development (TDD)
- Work in small, atomic steps using TDD:
  1. Write failing xUnit test.
  2. Write minimal implementation to pass.
  3. Run `dotnet test` to verify.
  4. Commit changes.

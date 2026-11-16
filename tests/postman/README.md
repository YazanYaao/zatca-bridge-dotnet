# Postman Collection & Newman E2E Smoke Tests

This directory is configured to support automated End-to-End (E2E) API smoke tests via [Newman](https://learning.postman.com/docs/running-collections/using-newman-cli/command-line-integration-with-newman/) in the CI/CD pipeline.

---

## 1. Enabling CI/CD E2E Tests

The GitHub Actions CI/CD workflow (`.github/workflows/ci.yml`) automatically detects and executes the collection if present.

To activate automated E2E pipeline checks:
1. Open your Postman workspace containing the ZatcaBridge requests.
2. Export the collection using **Collection v2.1 (recommended)** format.
3. Save the exported JSON file directly to this directory as:
   ```
   tests/postman/ZatcaBridge.postman_collection.json
   ```
4. Commit and push the file to trigger automated Newman execution during CI builds.

---

## 2. Collection Configuration

### Collection Variables
The collection must define or accept the following variable:
- `baseUrl`: The base URL of the running ZatcaBridge API instance (defaults to `http://localhost:5000` in CI).

### Key Test Endpoints
Ensure your collection tests cover the primary Minimal API endpoints:
- `POST /api/v1/invoices`
  - Issues standard B2B/B2C invoices, verifies UBL 2.1 generation, SHA-256 hash chaining, and outbox queuing.
  - Returns `201 Created` with invoice details.
- `GET /api/v1/invoices/chain/latest`
  - Returns the latest cryptographic hash chain record (`200 OK`) or `404 Not Found` if the ledger is empty.
  - Used by CI readiness probes.
- `POST /api/v1/outbox/process`
  - Triggers on-demand processing of pending outbox invoices against the ZATCA gateway.
  - Returns `200 OK` with processed count summary.

---

## 3. Running Locally with Newman

### Prerequisites
- Node.js (v18+)
- Newman CLI:
  ```bash
  npm install -g newman
  ```

### Step-by-Step Local Run
1. Start the API locally:
   ```bash
   dotnet run --project src/ZatcaBridge.Api --urls "http://localhost:5000"
   ```
2. In a separate terminal, execute Newman:
   ```bash
   newman run tests/postman/ZatcaBridge.postman_collection.json --env-var "baseUrl=http://localhost:5000"
   ```

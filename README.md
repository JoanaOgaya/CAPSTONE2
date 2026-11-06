# Finance Operations Management System (FOMS) & Financial Intelligence Layer

An enterprise-grade staff-only Finance Operations Management System (FOMS) with an AI-Powered Financial Intelligence Layer. The platform unifies billing computations, accounts receivable, staff-recorded payment validation, and AI document intelligence.

---

## 1. Project Overview

FOMS provides end-to-end financial operations workflows backed by strict database governance and verifiable audit logging. The architecture connects to:
- **Microsoft SQL Server (`FOMS_DB`)**: Official transactional source of truth for all users, rate cards, invoices, billing documents, accounts receivable, payments, official receipts, liquidations, settlements, and audit ledgers.
- **PostgreSQL (`foms_ai_results`)**: Dedicated analytical datastore for AI OCR outputs, document extractions, duplicate alerts, and credit risk evaluations.
- **MongoDB (`foms_trends_db`)**: High-throughput document store for AI scan metadata, activity telemetry, and trend snapshots.

---

## 2. Backend Scope

The FOMS backend engine enforces strict database persistence and enterprise security standards:
- **Authentication & RBAC**: Secure JWT-based authentication enforcing granular role-based access for Head Accountant, Accountant, Assistant of Finance Manager, and Finance Manager.
- **EMP-001 Finance Manager Flow**: Executive dashboard oversight, rate configuration and client accounts governance, invoice review and creation, accounts receivable and cash flow management, payment tracking, settlement validation, financial adjustment approval, liquidation oversight, and system-wide reporting.
- **EMP-002 Head Accountant Flow**: Invoice review, billing computation validation, accounts receivable monitoring, settlement validation, financial adjustments, and audit trail examination.
- **EMP-003 Accountant Flow**: Client accounts management, automated invoice generation from validated unbilled waybills, billing document preparation, payment recording, and official receipt tracking.
- **EMP-004 Assistant of Finance Manager Flow**: Liquidation report submission, supporting document inspection, expense verification against receipts, revision handling, and managerial endorsement.
- **Payment Collection**: Staff record check, bank transfer, or cash payments; the Finance Manager validates funds before AR is updated.
- **Report Exports**: Report workbooks are generated server-side as real `.xlsx` files and each export is audit-logged. Tabular exports accept up to 100 columns and 5,000 rows.
- **Retention**: A daily job soft-archives settled invoices and inactive clients after two years, logs each archive, and never hard-deletes data. Archived records are visible only to the Finance Manager and Head Accountant. Open invoice balances remain in AR and client balances; other internal records are retained without automatic deletion.
- **AI Duplicate Scan & Document Extraction**: Multi-stage validation accepting only authentic finance documents (OR, Invoice, Billing Statement, Statement of Account, Proof of Payment) with automatic HTTP 422 rejection of non-finance images (selfies, food, scenery).
- **Audit Logging & AI Output History**: Tamper-evident logging to MSSQL `dbo.AuditLogs` for all business actions, and safe querying of AI output history.
- **Zero Mock / Seed Data Rule**: Completely eliminated mock arrays and fallback fixtures. The UI renders real database data; when tables are empty, APIs return empty arrays (`[]`) and zero summaries.

---

## 3. Database Architecture (Polyglot Persistence)

| Database Engine | Host / Port | Target Database | Role & Scope |
|---|---|---|---|
| **Microsoft SQL Server** | `localhost\SQLEXPRESS` | `FOMS_DB` | **Official Source of Truth** for Users, Roles, Clients, ClientRates, Invoices, BillingDocuments, AccountsReceivable, Payments, PaymentHistory, OfficialReceipts, Liquidations, Settlements, FinancialAdjustments, and AuditLogs. |
| **PostgreSQL 15** | `localhost:5432` | `foms_ai_results` | Datastore for AI document OCR outputs, duplicate alerts, extracted financial entities, and risk scores. |
| **MongoDB 6.0** | `localhost:27017` | `foms_trends_db` | Telemetry logs, historical activity metrics, and time-series audit snapshots. |

---

## 4. Local Database Setup

### Microsoft SQL Server (Local SQLEXPRESS)
- **Server:** `localhost\SQLEXPRESS`
- **Database:** `FOMS_DB`
- **Authentication:** Windows Authentication (`Trusted_Connection=True;TrustServerCertificate=True;`)
- **Connection String:**
  ```text
  Server=localhost\SQLEXPRESS;Database=FOMS_DB;Trusted_Connection=True;TrustServerCertificate=True;
  ```

### PostgreSQL (Docker)
- **Host / Port:** `localhost:5432`
- **Database:** `foms_ai_results`
- **User:** `postgres`

### MongoDB (Docker)
- **URI:** `mongodb://localhost:27017`
- **Database:** `foms_trends_db`

---

## 5. Environment Setup

1. Copy `.env.example` to create your local `.env` file:
   ```bash
   cp .env.example .env
   ```
2. Configure local secrets and connection parameters in `.env`.
3. **Security Rule:** Never commit `.env` or real API keys to GitHub. `.gitignore` is pre-configured to exclude all `.env` files except `.env.example`.

---

## 6. Test Accounts & Role Credentials

The local backend authenticates these verified accounts against `dbo.Users` and `dbo.Roles` in `FOMS_DB`:

| Employee Number | Password | Role Name | System Access Scope |
|---|---|---|---|
Demo credentials are intentionally omitted. Use the employee number and password issued by your administrator.

---

## 7. Role-Based Backend Flows

### EMP-001 Finance Manager
- **Dashboard**: High-level executive metrics (total invoices billed, total invoice amount, outstanding AR, collection rate, cash inflow, duplicate alerts, due date alerts, cash flow analytics, top paying clients, needs attention queue).
- **Client Accounts**: Active client profiles and finance settings directly from `dbo.Clients` and `dbo.ClientFinanceSettings`.
- **Rate Configuration**: Delivery area rate cards (`dbo.ClientRates`), tax types (VATable/Zero-rated), VAT rate calculation, and agreed billing terms.
- **Waybills & POD Tracking**: Ingest and track delivery waybills (`dbo.Waybills`) with proof-of-delivery validation statuses.
- **Create Invoice**: Automated calculation from validated unbilled waybills and rate cards with tax calculations.
- **Invoice List & Invoice Review**: Real-time invoice statuses, managerial review queue, and settlement tracking.
- **Billing Validation**: Oversight of computed line items against waybills and rates.
- **Accounts Receivable**: Real-time balances, aging buckets (0-30, 31-60, 61-90, 90+ days), and manager approval queue.
- **Payments & Cash Flow**: Live cash inflow/outflow ledger, net cash flow tracking, and payment verification.
- **Official Receipts**: Chronological list of issued official receipts generated upon full payment settlement.
- **Settlement Validation & Record Settlement**: Courier trip settlement validation and recording of new settlement entries.
- **Adjustment Approval**: Review, approve, or reject financial adjustments and debit/credit memos with automatic AR updates.
- **Liquidation Reports**: Review endorsed employee expense liquidations and leftover cash/reimbursement balances.
- **Unique Documents & Review History**: Audit unique documents and investigate duplicate alert histories.
- **Reports & AI Output History**: Executive reporting across accounts aging, collections, cash flow, adjustments, liquidations, and invoices.
- **Audit Logs**: Tamper-evident, chronological audit logging from `dbo.AuditLogs`.

### EMP-002 Head Accountant
- **Dashboard**: Metrics for pending invoices, payment validation queue, AR balance, and recent audit trails.
- **Invoice List & Invoice Review**: Review invoices submitted by accountants; approve, reject, or return with remarks.
- **Billing Validation**: Audit computed line items against waybills and rates; approve, reject, or return.
- **Accounts Receivable**: Real-time balances, aging buckets, and collection prioritization.
- **Payment Validation**: Validate staff-recorded payments, update AR, log `PaymentHistory`, and generate ORs.
- **Settlements & Record Settlement**: Validate trip settlements or record new courier liquidation balances.
- **Adjustment Validation**: Review debit/credit memo adjustment requests.
- **Unique Documents & For Review**: Examine AI-flagged documents, resolve or escalate duplicate alerts.
- **Reports & Audit Logs**: Full chronological audit trail from `dbo.AuditLogs`.

### EMP-003 Accountant
- **Dashboard**: Invoices created, pending billing items, AR summary, and pending payments.
- **Client Accounts**: Active client profiles and finance settings directly from `dbo.Clients`.
- **Create Invoice**: Automated calculation from validated unbilled waybills and rate cards (`dbo.ClientRates`).
- **Invoice List**: Chronological invoice records with live balances and settlement states.
- **Billing Validation**: Prepare and submit billing batches for Head Accountant endorsement.
- **Accounts Receivable & Payments**: View AR ledgers, record payments, and inspect payment histories.
- **Official Receipts**: Chronological list of issued official receipts generated upon full payment.
- **Settlements & Financial Adjustments**: Record settlements and create adjustment requests.
- **Duplicate Scan & Extraction**: Upload finance documents with strict format validation and OCR entity extraction.

### EMP-004 Assistant of Finance Manager
- **Dashboard**: Pending liquidations, returned items, endorsed items, and scan statistics.
- **Liquidation Reports**: View, review, return with correction remarks, or endorse liquidations to the Finance Manager.
- **Duplicate Scan & Unique Documents**: Scan liquidation documents and view confirmed unique document records.
- **Flagged Duplicates & Review History**: Audit duplicate alerts with similarity scores and audit remarks.
- **Document Extraction**: Extract receipt numbers, amounts, and dates from valid liquidation documents.
- **Reports & AI Output History**: Generate liquidation summaries and review AI processing logs.

---

## 8. API Endpoint Summary

### Authentication
- `POST /api/auth/login` - Authenticate employee credentials and return JWT with user profile.

### Health & Diagnostics
- `GET /api/health/database` - Verify MSSQL connection to `localhost\SQLEXPRESS` and `FOMS_DB`.
- `GET /api/health/finance-manager` - Verify EMP-001 Finance Manager status.
- `GET /api/health/head-accountant` - Verify EMP-002 Head Accountant status.
- `GET /api/health/accountant` - Verify EMP-003 Accountant status.
- `GET /api/health/assistant-finance-manager` - Verify EMP-004 Assistant FM status.
- `GET /api/health/no-mock-data` - Confirm mock data and seed fallbacks are disabled.

### EMP-001 Finance Manager (`/api/finance-manager/*`)
- `GET /api/finance-manager/dashboard`
- `GET /api/finance-manager/clients`
- `GET /api/finance-manager/rate-configurations`
- `GET /api/finance-manager/waybills`
- `POST /api/finance-manager/waybills`
- `GET /api/finance-manager/invoices/eligible-clients`
- `POST /api/finance-manager/invoices/create`
- `GET /api/finance-manager/invoices`
- `GET /api/finance-manager/billing-validation`
- `GET /api/finance-manager/invoice-review`
- `GET /api/finance-manager/accounts-receivable`
- `GET /api/finance-manager/payments`
- `GET /api/finance-manager/official-receipts`
- `GET /api/finance-manager/settlements/pending`
- `GET /api/finance-manager/settlements`
- `POST /api/finance-manager/settlements`
- `GET /api/finance-manager/adjustments`
- `POST /api/finance-manager/adjustments/{id}/approve`
- `POST /api/finance-manager/adjustments/{id}/reject`
- `GET /api/finance-manager/liquidations`
- `GET /api/finance-manager/cash-flow`
- `GET /api/finance-manager/ai/unique-documents`
- `GET /api/finance-manager/ai/review-history`
- `POST /api/finance-manager/reports/generate`
- `GET /api/finance-manager/audit-logs`
- `GET /api/finance-manager/ai/output-history`

### EMP-002 Head Accountant (`/api/head-accountant/*`)
- `GET /api/head-accountant/dashboard`
- `GET /api/head-accountant/invoices`
- `GET /api/head-accountant/billing-validation`
- `POST /api/head-accountant/billing-validation/{id}/approve`
- `POST /api/head-accountant/billing-validation/{id}/reject`
- `POST /api/head-accountant/billing-validation/{id}/return`
- `GET /api/head-accountant/invoice-review`
- `POST /api/head-accountant/invoice-review/{invoiceId}/approve`
- `POST /api/head-accountant/invoice-review/{invoiceId}/reject`
- `POST /api/head-accountant/invoice-review/{invoiceId}/return`
- `GET /api/head-accountant/accounts-receivable`
- `GET /api/head-accountant/payment-validation`
- `POST /api/head-accountant/payments/{paymentId}/validate`
- `POST /api/head-accountant/payments/{paymentId}/reject`
- `POST /api/head-accountant/payments/{paymentId}/return`
- `GET /api/head-accountant/settlements`
- `POST /api/head-accountant/settlements/{settlementId}/validate`
- `POST /api/head-accountant/settlements/{settlementId}/reject`
- `POST /api/head-accountant/settlements/{settlementId}/return`
- `GET /api/head-accountant/record-settlement`
- `POST /api/head-accountant/record-settlement`
- `GET /api/head-accountant/adjustment-validation`
- `POST /api/head-accountant/adjustments/{id}/validate`
- `POST /api/head-accountant/adjustments/{id}/reject`
- `POST /api/head-accountant/adjustments/{id}/return`
- `GET /api/head-accountant/ai/unique-documents`
- `GET /api/head-accountant/for-review`
- `POST /api/head-accountant/for-review/{id}/approve`
- `POST /api/head-accountant/for-review/{id}/reject`
- `POST /api/head-accountant/for-review/{id}/dismiss-flag`
- `POST /api/head-accountant/for-review/{id}/escalate`
- `POST /api/head-accountant/for-review/{id}/mark-resolved`
- `GET /api/head-accountant/review-history`
- `POST /api/head-accountant/reports/generate`
- `GET /api/head-accountant/audit-logs`
- `GET /api/head-accountant/ai/output-history`

### EMP-003 Accountant (`/api/accountant/*`)
- `GET /api/accountant/dashboard`
- `GET /api/accountant/clients`
- `GET /api/accountant/invoices/eligible-clients`
- `POST /api/accountant/invoices/create`
- `GET /api/accountant/invoices`
- `GET /api/accountant/billing-validation`
- `POST /api/accountant/billing-validation/{id}/submit`
- `POST /api/accountant/billing-validation/{id}/return`
- `GET /api/accountant/accounts-receivable`
- `GET /api/accountant/payments`
- `POST /api/accountant/payments/record`
- `GET /api/accountant/payments/history/{invoiceId}`
- `GET /api/accountant/official-receipts`
- `GET /api/accountant/settlements`
- `GET /api/accountant/record-settlement`
- `POST /api/accountant/record-settlement`
- `GET /api/accountant/financial-adjustments`
- `POST /api/accountant/financial-adjustments`
- `POST /api/accountant/ai/duplicate-scan`
- `GET /api/accountant/ai/unique-documents`
- `GET /api/accountant/ai/flagged-duplicates`
- `GET /api/accountant/review-history`
- `POST /api/accountant/ai/document-extraction`
- `GET /api/accountant/ai/document-extraction/history`
- `POST /api/accountant/reports/generate`
- `GET /api/accountant/ai/output-history`

### EMP-004 Assistant of Finance Manager (`/api/assistant-finance-manager/*`)
- `GET /api/assistant-finance-manager/dashboard`
- `GET /api/assistant-finance-manager/liquidations`
- `POST /api/assistant-finance-manager/liquidations/submit`
- `POST /api/assistant-finance-manager/liquidations/{id}/review`
- `POST /api/assistant-finance-manager/liquidations/{id}/return`
- `POST /api/assistant-finance-manager/liquidations/{id}/endorse`
- `POST /api/assistant-finance-manager/ai/duplicate-scan`
- `GET /api/assistant-finance-manager/ai/unique-documents`
- `GET /api/assistant-finance-manager/ai/flagged-duplicates`
- `GET /api/assistant-finance-manager/review-history`
- `POST /api/assistant-finance-manager/ai/document-extraction`
- `GET /api/assistant-finance-manager/ai/document-extraction/history`
- `POST /api/assistant-finance-manager/reports/generate`
- `GET /api/assistant-finance-manager/ai/output-history`

### Staff payment collection

FOMS has no client login, client portal, or payment gateway. Clients pay by check, bank transfer, or cash. Staff record the payment and supporting proof; only the Finance Manager validates confirmed funds. Short, excess, duplicate, and unidentified payments remain in the exception workflow until resolved; partial settlement is not recorded.

Client refunds are not supported. An approved credit memo reduces an open receivable, while credit on an already-paid invoice is held as client credit and applied to the next invoice. Legacy refund and payment-provider records remain in the database for historical retention only; current routes and configuration do not process them. Additive migrations normalize stored role aliases and mark retired payment/refund records as legacy without dropping history.

---

## 10. Strict No Mock Data Rule

- **Real Database Records Only**: All API endpoints read directly from Microsoft SQL Server (`FOMS_DB`) or PostgreSQL (`foms_ai_results`).
- **Empty State Governance**: When a database table has no rows, the backend returns:
  ```json
  {
    "success": true,
    "count": 0,
    "data": []
  }
  ```
  The frontend displays its standard "No records found" empty state. No mock arrays, hardcoded fallback values, or demo fixtures are rendered.

---

## 11. Run Backend Locally

```powershell
# Restore dependencies
dotnet restore foms-backend/FOMS.Api/FOMS.Api.csproj

# Build backend solution
dotnet build foms-backend/FOMS.Api/FOMS.Api.csproj

# Run backend Web API
cd foms-backend/FOMS.Api
dotnet run
```
Backend API will be accessible on `http://localhost:5007` (or configured port).

---

## 12. Verify Database Connection

Verify active connectivity to SQL Server:
```bash
curl http://localhost:5007/api/health/database
```
Expected response:
```json
{
  "success": true,
  "database": "FOMS_DB",
  "provider": "Microsoft SQL Server",
  "server": "localhost\\SQLEXPRESS",
  "status": "Connected"
}
```

Verify role accessibility:
```bash
curl http://localhost:5007/api/health/finance-manager
curl http://localhost:5007/api/health/head-accountant
curl http://localhost:5007/api/health/accountant
curl http://localhost:5007/api/health/assistant-finance-manager
curl http://localhost:5007/api/health/no-mock-data
```

---

## 13. Git Safety & Secrets Management

- **Live Secrets Exclusion**: API keys, JWT secrets, and passwords must reside only in deployment secret storage.
- **Committed Files**: Only `.env.example` (containing safe placeholders) is tracked in version control.
- **Git Push Verification**:
  1. Confirm `.env` is listed in `.gitignore` and `git status` does not show `.env`.
  2. Confirm `.env.example` contains only placeholders.
  3. Confirm no raw credentials exist in `appsettings.json`.
  4. Push code safely to GitHub remote branches.

---

## 14. Cash Outflow Approval & Escalation Workflow

### 14.1 Objective & Architecture
The Cash Outflow Approval & Escalation Workflow enforces a strict dual-level approval hierarchy backed by Microsoft SQL Server (`FOMS_DB`):
```text
Record Cash Outflow 
  → Assistant of Finance Manager Review (Level 1)
  → Threshold Check:
      - Amount <= Assistant Limit (₱10,000.00):
          Assistant Approves → Status: Approved by Assistant → Cash Outflow Posted (is_posted = 1)
      - Amount > Assistant Limit (₱10,000.00):
          Assistant Approval BLOCKED (HTTP 403 server-side)
          Assistant Escalates → Level 2 (Pending Finance Manager Approval)
          Finance Manager Reviews → Approves / Rejects / Returns
  → Cash Flow Ledger Synchronized → 100% Exact Frontend Display
```

### 14.2 Database Tables
- `dbo.ApprovalThresholdSettings`: Configurable threshold limits for roles and transaction types.
- `dbo.CashOutflowApprovalRequests`: Multi-level approval requests tracking amounts, levels, assigned approvers, and escalation status.
- `dbo.CashOutflowApprovalHistory`: Audit timeline logging all actions (Submitted, Approved, Escalated, Rejected, Returned) with timestamps and actor details.
- `dbo.CashOutflows`: Transactional cash outflow ledger updated atomically upon authorized final approval.
- `dbo.AuditLogs`: System-wide audit log trail for governance and compliance.

### 14.3 API Endpoints
| HTTP Method | Route | Description | Authorization |
|---|---|---|---|
| `POST` | `/api/cash-outflows` | Submit new cash outflow request | Authenticated Users |
| `GET` | `/api/cash-outflows` | Retrieve cash outflow records | Authenticated Users |
| `GET` | `/api/approvals/pending` | Filtered pending approvals queue | Assistant FM / FM |
| `GET` | `/api/approvals/{id}` | Retrieve single approval request detail | Authenticated Users |
| `POST` | `/api/approvals/{id}/approve` | Assistant approves request (≤ threshold only) | Assistant FM |
| `POST` | `/api/approvals/{id}/escalate` | Endorse & escalate high-value request to Level 2 | Assistant FM |
| `GET` | `/api/finance-manager/escalated-approvals` | Level 2 escalated approvals queue | Finance Manager (EMP-001) |
| `POST` | `/api/finance-manager/escalated-approvals/{id}/approve` | Finance Manager approves Level 2 outflow | Finance Manager (EMP-001) |
| `POST` | `/api/finance-manager/escalated-approvals/{id}/reject` | Finance Manager rejects outflow (requires reason) | Finance Manager (EMP-001) |
| `POST` | `/api/finance-manager/escalated-approvals/{id}/return` | Finance Manager returns request for correction | Finance Manager (EMP-001) |
| `GET` | `/api/health/cash-outflow` | Outflows and threshold health check | Authenticated Users |
| `GET` | `/api/health/approval-workflow` | Queue counts and audit history health check | Authenticated Users |

### 14.4 Threshold Configuration
Threshold settings are stored in `dbo.ApprovalThresholdSettings`. To view or adjust the limit:
```sql
USE FOMS_DB;
GO

-- View current threshold settings
SELECT * FROM dbo.ApprovalThresholdSettings WHERE transaction_type = 'CashOutflow' AND is_active = 1;

-- Update Assistant approval threshold to ₱20,000.00
UPDATE dbo.ApprovalThresholdSettings
SET maximum_amount = 20000.00, updated_at = SYSUTCDATETIME(), updated_by = 'Finance Admin'
WHERE transaction_type = 'CashOutflow' AND role_name = 'Assistant of Finance Manager';
```

### 14.5 Database-to-Frontend Consistency Verification
Run the automated end-to-end workflow verification script:
```powershell
powershell -File "scratch/test_workflow.ps1"
```
Or execute SQL verification directly:
```sql
USE FOMS_DB;
GO

-- 1. Compare approval requests and posting status
SELECT transaction_reference, amount, status, approval_level, is_posted
FROM dbo.CashOutflowApprovalRequests
ORDER BY created_at DESC;

-- 2. Verify posted outflows
SELECT CashOutflowId, SourceReference, payee, amount, status
FROM dbo.CashOutflows
ORDER BY created_at DESC;

-- 3. Verify cash outflow sum matches frontend summary
SELECT SUM(amount) AS TotalOutflow
FROM dbo.CashOutflows
WHERE status IN ('Approved', 'Approved by Assistant', 'Approved by Finance Manager', 'Completed');
```

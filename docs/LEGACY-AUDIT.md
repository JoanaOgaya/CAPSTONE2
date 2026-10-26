# FOMS Legacy System Audit & Migration Mapping (MIG-01)
**Document Version:** 1.0  
**Date:** October 26, 2026  
**Author:** Lead Full-Stack Engineer, Speedex FOMS  
**Authority:** FOMS Merged Requirements and Recommended Flow (Capstone 2) & Speedex Design System v2.0 (OneUI)

---

## 1. Executive Summary & Data Classification

### 1.1 Data Classification: Test vs Real Data
- **Audit Finding:** All records in `FOMS_DB` (and all seeding scripts: `seed_azure_approvals.sql`, `setup_approvals.sql`, `foms-backend/database/mssql/init.sql`, `scripts/seed_create_invoice_data.sql`, `scripts/seed_staff.py`) consist **100% of synthetic test data**.
  - Transactions use mock IDs (e.g. `CO-TEST-2851`, `CO-HIGH-4014`, `WB-2026-0001`).
  - Vendors and payees are dummy placeholders ("National Book Store", "Dell Systems Philippines", "Unverified Vendor").
  - Test employee accounts use mock employee numbers (`EMP-001` to `EMP-004`) with uniform dummy passwords (`Password@123`).
  - Client accounts are mock e-commerce names ("Shopee Philippines", "Lazada E-Services", "Zalora Philippines").
- **Recommendation (Approved Approach):** Rather than performing fragile data-migration mappings on corrupted test data, perform a **clean database reset and reseed** with official Capstone 2 schema, standard seed rate cards, and mock clients. Test logins (`EMP-001` through `EMP-004`) remain preserved behind a development flag until the central STARS authentication contract is finalized and connected.

---

## 2. Audit of the 16 Suspect Legacy Flows

Every legacy behavior identified on pages 34–35 of the Merged Requirements document was audited against the codebase.

| # | Suspect Legacy Flow | Status in Codebase | Evidence / Code Location | Action Required |
|---|---------------------|--------------------|--------------------------|-----------------|
| **1** | Partial payments or a "Partially Paid" status | **EXISTS** | Legacy `InvoicesController` balance logic and frontend payment forms permit partial settling. | **REMOVE:** Enforce exact invoiced amount only (PAY-04). Any variance routes to `FinancialExceptions` queue. |
| **2** | Anyone other than Finance Manager validates payments | **EXISTS** | `roleAccess.ts` and `Payments.tsx` permit Head Accountant (`HA`) to verify and validate payments. | **REWORK:** Strictly restrict payment validation to Financial Manager only (PAY-03). Head Accountant is view-only. |
| **3** | Webhook, DMS, STARS or integration sets invoice to "Paid" | **EXISTS** | `PaymongoWebhookController.cs` automatically marks invoices Paid upon receiving webhook events. | **REWORK:** Webhook sets status to "Paid at Gateway" only (SPD-03). Only FM validation transitions invoice to "Paid" (PAY-04, SPD-04). |
| **4** | Invoice created directly when delivery completes | **EXISTS** | `DmsIntegrationController.cs` (line 57) sets `AutoGenerateInvoice = true` upon delivery completion webhook. | **REWORK:** Remove auto-invoice generation. Completed delivery creates a pending POD record (POD-01), validated by Accountant, then billed in scheduled batch runs (BIL-01). |
| **5** | Document status and payment status in one mixed field | **EXISTS** | `InvoicesController.MapToFrontend` combines review and payment status into a single `status` property. | **REWORK:** Split into two discrete database and API fields: `document_status` (Draft, For Review, Approved, Sent, Received, Disputed, Void) and `payment_status` (Unpaid, Paid) per BIL-09. |
| **6** | Overdue or aging stored as a status | **EXISTS** | `InvoicesController.cs` sets `pStatus = i.DaysOverdue > 0 ? "Overdue"`. | **REWORK:** Eliminate stored Overdue/Aging fields. Aging buckets (1–30, 31–60, 61–90, 90+) and Overdue are dynamically derived from `due_date` (ARC-03). |
| **7** | Approved invoices can be edited | **EXISTS** | `InvoicesController.UpdateInvoice` (`PUT /api/invoices/{id}`) allows editing amounts and dates with no guard against Approved state. | **REWORK:** Lock invoice once `document_status == Approved` (BIL-10). Corrections must use Credit Memos (ADJ-01). Voids require documented reason. |
| **8** | Float money, typed-in rates, or a bare total | **EXISTS** | Legacy SQLite entities stored float/double; manual rate typing allowed in `InvoiceCreation.tsx`. | **REWORK:** Store money as `decimal(18,2)` with single per-line rounding (NFR-12). Rates auto-computed from `ClientRates` per formula; bare totals banned (BIL-02, BIL-03). |
| **9** | Credit memos or discounts treated as cash outflow | **EXISTS** | Legacy adjustment controller stubs and cash outflow summaries conflated credit adjustments with cash outflows. | **REWORK:** Credit memos and discounts reduce accounts receivable ONLY (ADJ-03). Client refunds are separate cash outflows. |
| **10** | Outflow posted at liquidation | **EXISTS** | Partial/inconsistent handling: legacy flows posted outflow at liquidation; current `CashAdvancesController` posts at disbursement but liquidation still posts reimbursements improperly. | **REWORK:** Outflow is strictly posted at cash advance release (CSH-04). Liquidation converts Committed to Actual and releases unused commitment (FIN-09). |
| **11** | Official Receipt as a separate generated document | **EXISTS** | `OfficialReceiptsController.cs` maintains separate OR generation cycles and sequences. | **REWORK:** The collector's reference number serves as the OR and links directly to payment and invoice (PAY-07). Principal tax document is the invoice (EOPT). |
| **12** | Receivable created at wrong time or due date not set from received date | **EXISTS** | Invoices automatically generated receivable balances at Draft creation; due dates were calculated from creation date. | **REWORK:** Receivable created only when invoice is marked `Sent` (ARC-01). Due date is strictly calculated as `received_date + 30 days` upon signed copy return (ARC-02). |
| **13** | Missing duplicate alerts or zero/negative guards | **EXISTS** | Ingestion endpoints allow duplicate waybill submissions without real-time blocking; zero/negative totals pass silently. | **REWORK:** Implement blocking duplicate pre-alerts (POD-02, PAY-05, AIL-01) and trap zero/negative computations into `FinancialExceptions` (BIL-05). |
| **14** | Role gaps, client seeing other client amounts, editable audit log | **EXISTS** | `roleAccess.ts` lacks Client role; no client-tenant filtering exists on APIs; audit logs lacked immutability constraints. | **REWORK:** Enforce 5 canonical roles; filter client endpoints to client-owned data (IAM-06); make audit logs strictly append-only (AUD-01). |
| **15** | PayMongo gateway wired into core flow | **EXISTS** | `SpeedPay.tsx` and `PaymongoWebhookController.cs` are wired directly into primary frontend routing and payment handling. | **REWORK:** Move PayMongo gateway into a stretch folder (`src/stretch/paymongo`), behind feature flag `VITE_ENABLE_PAYMONGO=false`. Core SpeedPay is client proof-of-payment upload (SPD-01, SPD-08). |
| **16** | UI breaks Design System v2.0 | **EXISTS** | Existing frontend uses Montserrat/Inter fonts, bare icon action buttons, CSS spinners, heavy box shadows, and developer terms. | **REWORK:** Apply Design System v2.0 tokens (Outfit font, flat corporate layout, skeleton loaders, no naked icons, plain business language) during Phase 1 & 2. |

---

## 3. Frontend Pages Audit (`foms-frontend`)

| Page Component | Route / Path | Action | Rationale & Capstone 2 Target |
|----------------|--------------|--------|-------------------------------|
| `Dashboard.tsx` | `/foms/dashboard` | **REWORK** | Align with FIN-07 executive metrics, OneUI tokens, Outfit font, and Recharts. Roles: FM, HA. |
| `AccountantDashboard.tsx` | `/accountant-dashboard` | **REMOVE** | Consolidate into unified `/foms/dashboard` with role-based view permissions. |
| `HeadAccountantDashboard.tsx` | `/head-accountant-dashboard` | **REMOVE** | Consolidate into unified `/foms/dashboard`. |
| `ClientManagement.tsx` | `/foms/clients` | **REWORK** | Align with CRM-01 to CRM-05, signed proposal attachment check (CRM-02), DataTable standards. |
| `RateConfiguration.tsx` | `/rate-configuration` | **REWORK** | Merge into `/foms/clients` as the Rates tab/drawer (CRM-03 area rates, effective dates, approver). |
| `Waybills.tsx` | `/foms/waybills` | **REWORK** | Align with POD-01 to POD-05, StatusBadge (Validated, CTC, On Hold, Rejected), duplicate alerts. |
| `BillingValidation.tsx` | `/billing-validation` | **REWORK** | Merge into `/foms/invoices` as Head Accountant verification tab (BIL-08). |
| `InvoiceCreation.tsx` | `/foms/invoices/create` | **REWORK** | Implement exact billing computation formula (CW, Freight, Valuation 1%, VAT 12%, Fuel matrix), zero/negative guard (BIL-02, BIL-05). |
| `InvoicingDesk.tsx` | `/foms/invoices` | **REWORK** | Master billing and invoice management table (BIL-01, BIL-09, BIL-10 immutable approved invoices). |
| `InvoiceReview.tsx` | `/invoice-review` | **REWORK** | Consolidate into `/foms/invoices` review modal/drawer. |
| *InvoicePrintView (New)* | `/foms/print/invoice/:id` | **KEEP / BUILD** | Dedicated standalone route outside `DashboardLayoutShell` for clean A4 printing without system UI (BIL-04). |
| `AccountsReceivable.tsx` | `/foms/accounts-receivable` | **REWORK** | Align with ARC-01 to ARC-07, derived aging buckets (1–30, 31–60, 61–90, 90+), Excel export. |
| `StarsCollectionWorkflow.tsx` | `/stars-collection` | **REWORK** | Integrate into Accounts Receivable as STARS collection follow-up tracker (ARC-05, ARC-06). |
| `Payments.tsx` | `/foms/payments` | **REWORK** | Align with PAY-01 to PAY-09, FM validation gate (PAY-03), exact invoice settling, exception queue. |
| `Receipts.tsx` | `/receipts` | **REWORK** | Consolidate into Payments as collector OR reference view (PAY-07). |
| `ReceiptModal.tsx` | Modal Component | **REWORK** | Reusable receipt modal showing collector OR reference and linked invoice. |
| `Settlements.tsx` | `/foms/settlements` | **REWORK** | Courier trip settlement validation and ledger (CSH-02). |
| `RecordSettlement.tsx` | `/record-settlement` | **REWORK** | Consolidate into Settlements drawer/modal. |
| `SpeedPayValidation.tsx` | `/speedpay-validation` | **REWORK** | Merge into Payments validation tab (proof-of-payment verification queue). |
| `SpeedPay.tsx` | `/foms/portal` | **REWORK** | Transform into Client Portal with proof-of-payment upload (SPD-01, SPD-06, SPD-08). Move PayMongo code to stretch folder. |
| `FinancialAdjustments.tsx` | `/foms/adjustments` | **REWORK** | Align with ADJ-01 to ADJ-04 (credit memos reduce AR only, FM approves adjustments, CEO approves discounts). |
| `CashFlowManagement.tsx` | `/foms/cash-flow` | **REWORK** | Align with CSH-01 to CSH-03 (bank vs cash on hand, net cash calculation, outflow at release). |
| `CashAdvanceLiquidationPortal.tsx` | `/cash-advance-portal` | **REMOVE** | Split into Liquidation (/foms/liquidation) and Budget/Approvals (/foms/budget). |
| `MyCashAdvances.tsx` | `/my-cash-advances` | **REWORK** | Consolidate into Cash Advance & Liquidation module. |
| `ApprovalsQueue.tsx` | `/approvals` | **REWORK** | Align with FIN-10 budget overrides and CSH-09 large cash outflow approval (recording CEO evidence). |
| `LiquidationValidation.tsx` | `/foms/liquidation` | **REWORK** | Align with CSH-05 to CSH-08 (Assistant validation, fixed rejection reasons, variance computation). |
| `AIDuplicateScan.tsx` | `/foms/ai-insights/duplicates` | **REWORK** | Merge into unified AI Decision Support module (AIL-01). |
| `AIOutputHistory.tsx` | `/foms/ai-insights/history` | **REWORK** | Merge into unified AI Decision Support module (AIL-06). |
| `UniqueDocuments.tsx` | `/unique-documents` | **REMOVE** | Consolidate into AI Decision Support tabs. |
| `FlaggedDuplicates.tsx` | `/flagged-duplicates` | **REMOVE** | Consolidate into AI Decision Support alerts tab. |
| `ReviewHistory.tsx` | `/review-history` | **REMOVE** | Consolidate into AI Decision Support history tab. |
| `ManualReviewQueue.tsx` | `/manual-review-queue` | **REMOVE** | Consolidate into AI Decision Support review tab. |
| `ForReview.tsx` | `/for-review` | **REMOVE** | Redundant duplicate review route. |
| `DocumentExtraction.tsx` | `/document-extraction` | **MOVE TO STRETCH**| Move behind feature flag in stretch folder (AIL-04 document checking). |
| `Reports.tsx` | `/foms/reports` | **REWORK** | Single consolidated reports page with dropdown (Receivables, Payments, Cash/Expenses, Budget, Profit/Loss) and Excel/PDF export (FIN-04, FIN-05, FIN-06). |
| `AuditLogs.tsx` | `/foms/audit-log` | **REWORK** | Append-only audit log viewer with export capability (AUD-01 to AUD-03). |
| `DmsBillingIntake.tsx` | `/dms-billing-intake` | **REWORK** | Merge into Waybills and POD intake workflow. |
| `LoginPage.tsx` | `/login` | **REWORK** | Align with Design System v2.0, STARS central login adapter, preserve EMP test accounts behind dev flag. |
| `ProfilePage.tsx` | `/profile` | **REWORK** | Align with IAM-04 (user profile, role badge, recent login history). |
| `NotificationsPage.tsx` | `/notifications` | **REWORK** | Align with NTF-01 and Design System v2.0. |
| *Budget & Company Finance (New)* | `/foms/budget` | **KEEP / BUILD** | Monthly budget allocation grid, Committed/Actual/Available/Remaining, transfers, carry-overs (FIN-01, FIN-02, FIN-08 to FIN-13). |

---

## 4. Backend Controllers Audit (`foms-backend`)

### 4.1 Core Domain Controllers (KEEP & REWORK)
1. `InvoicesController.cs` – **REWORK**: Split document status and payment status (BIL-09); lock approved invoices (BIL-10); remove auto-set Paid; enforce exact decimal computations.
2. `PaymentsController.cs` – **REWORK**: Restrict validation to FM only (PAY-03); enforce exact invoice settling (PAY-04); send variances to exception queue; duplicate reference alerts (PAY-05).
3. `BillingRatesController.cs` – **REWORK**: Support area rate configurations (NCR, Luzon, Visayas, Mindanao), minimum/excess kg, effective dates, approvers (CRM-03).
4. `ClientsController.cs` – **REWORK**: Support client management (CRM-01..05), signed proposal requirement (CRM-02), and strict client multi-tenant isolation (IAM-06).
5. `LiquidationsController.cs` – **REWORK**: Assistant validation (CSH-07), itemized expenses (CSH-05), fixed rejection reasons, variance calculations (CSH-08).
6. `CashAdvancesController.cs` – **REWORK**: Budget checks against Available (FIN-08), reserve Committed (FIN-09), post cash outflow at disbursement (CSH-04).
7. `SettlementsController.cs` – **REWORK**: Courier trip settlements (CSH-02).
8. `OfficialReceiptsController.cs` – **REWORK**: Collector reference number linked to payment and invoice (PAY-07).
9. `AdjustmentsController.cs` – **REWORK**: Credit memos reduce AR only (ADJ-01, ADJ-03); FM approves adjustments, CEO approves discounts (ADJ-02); remove DELETE endpoint (AUD-01).
10. `CashFlowController.cs` – **REWORK**: Track bank vs cash on hand separately (CSH-01); net cash formula (CSH-03); post outflows at release.
11. `ReportsController.cs` – **REWORK**: Implement single consolidated report endpoint for Receivables, Payments, Cash/Expenses, Budget, Profit or Loss (accrual & cash) with Excel export (FIN-04..06).
12. `AuditLogsController.cs` – **REWORK**: Append-only audit trail logging user, timestamp, before/after values, report exports (AUD-01).
13. `ApprovalsController.cs` – **REWORK**: Multi-level threshold approval for budget overrides and large cash outflows, recording CEO approval evidence (FIN-10, CSH-09).
14. `DmsIntegrationController.cs` – **REWORK**: Idempotency key handling; remove auto-invoice creation; ingest waybills as Pending POD (POD-01).
15. `IntegrationController.cs` – **REWORK**: STARS invoice endorsement, client acknowledgment, overdue routing.
16. `AiDuplicatesController.cs`, `AiOutputsController.cs`, `AiFeaturesController.cs` – **REWORK**: Similarity duplicate checks (AIL-01), receivable risk scoring (AIL-02), collection priority (AIL-03).
17. `AuthController.cs` – **REWORK**: Central STARS JWT auth adapter; support 5 roles; preserve dev test accounts behind feature flag; 15-minute inactivity session expiration (IAM-01..03).
18. `UsersController.cs`, `RolesController.cs` – **REWORK**: Scope to 5 canonical roles.
19. `SpeedPaySubmissionsController.cs`, `SpeedPaySharedController.cs` – **REWORK**: Client proof-of-payment uploads (deposit slip, screenshot) routed to FM validation (SPD-08).
20. *BudgetController (New)* – **BUILD**: Monthly budget allocations, transfers, carry-overs, and variance checks (FIN-01, FIN-02, FIN-08..13).
21. *FinancialExceptionsController (New)* – **BUILD**: Dedicated queue for zero/negative amounts, short/over payments, and duplicate waybill holds (BIL-05, PAY-04).

### 4.2 Stretch Modules (MOVE TO STRETCH FOLDER & FEATURE FLAG)
1. `PaymongoWebhookController.cs` – **MOVE TO STRETCH** (`foms-backend/src/stretch/paymongo`): Webhook marks "Paid at Gateway" only (SPD-03, SPD-04). Flagged off by default.
2. `SpeedPayTransactionsController.cs` – **MOVE TO STRETCH**: PayMongo PaymentIntent creation (SPD-02).
3. `SpeedPayController.cs` – **MOVE TO STRETCH**: Direct gateway checkout.

### 4.3 Out-of-Scope & Redundant Controllers (REMOVE / RETIRE)
1. `PayrollController.cs` & `PayslipsController.cs` – **REMOVE**: Payroll is explicitly out of scope per Open Question #3.
2. `BankAdviceController.cs` – **REMOVE**: Out-of-scope legacy stub.
3. `TicketsController.cs` & `PaymentConcernTicketsController.cs` – **REMOVE**: Customer support tickets belong to CRM/operations, outside FOMS financial scope.
4. `StatementOfAccountsController.cs` – **REMOVE**: Redundant with unified Invoices and Reports.
5. `DeliveryPaymentValidationsController.cs`, `FinancePaymentsController.cs`, `PaymentCollectionsController.cs` – **REMOVE**: Redundant legacy duplicates of `PaymentsController`.
6. `AccountingController.cs`, `AnalyticsController.cs`, `ExpensesController.cs` – **REMOVE**: Empty legacy controller stubs.
7. `ShipmentPricingController.cs`, `ShipmentRecordsController.cs` – **REMOVE**: Legacy SQLite pricing tables superseded by `ClientRates` and `Waybills`.
8. `AccountantController.cs`, `HeadAccountantController.cs`, `FinancialManagerController.cs`, `AssistantFinanceManagerController.cs` – **PHASE OUT / REMOVE**: Monolithic legacy controllers containing duplicate logic of the resource controllers. Consolidate clean endpoints into domain controllers.

---

## 5. Database Tables Audit (`FOMS_DB` MSSQL)

| Table Name | Action | Details & Capstone 2 Target |
|------------|--------|-----------------------------|
| `dbo.Roles` | **KEEP** | 5 canonical roles: Financial Manager, Head Accountant, Accountant, Assistant of Financial Manager, Client. |
| `dbo.Users` | **KEEP** | Users with hashed credentials, employee numbers, role linkage, active flag. |
| `dbo.Clients` | **KEEP** | Client profiles with TIN, address, billing schedule, signed proposal flag (CRM-01, CRM-02). |
| `dbo.ClientFinanceSettings` | **KEEP** | Billing terms, credit terms, tax type (VATable/Zero-rated), ODA flag (CRM-03). |
| `dbo.ClientRates` | **KEEP** | Area rates (NCR, Luzon, Visayas, Mindanao), minimum/excess rates, fuel matrix, effective dates, approver. |
| `dbo.Waybills` | **KEEP** | Waybill number, delivery status, POD status (Pending, Validated, CTC, On Hold, Rejected), lock to invoice (POD-01..05). |
| `dbo.ProofOfDeliveries` | **KEEP** | Image URL, counterpart verification, remarks, check timestamps. |
| `dbo.BillingDocuments` | **KEEP** | Batch billing run metadata and generated invoice documents (BIL-01). |
| `dbo.Invoices` | **REWORK** | Discrete `document_status` and `payment_status` (BIL-09); sequential numbering; immutable after Approved. |
| `dbo.InvoiceLineItems` | **KEEP** | Itemized waybills, weights, rate calculations, taxes, surcharges (BIL-02, BIL-03). |
| `dbo.InvoiceComputations` | **KEEP** | Detailed mathematical breakdown of each invoice step. |
| `dbo.AccountsReceivable` | **REWORK** | Receivable recorded at `Sent` (ARC-01); due date derived from `received_date + 30 days` (ARC-02); dynamic aging. |
| `dbo.Payments` | **REWORK** | Payment channel, exact amount check (PAY-04), FM validation fields (PAY-03), collector OR link (PAY-07). |
| `dbo.PaymentHistory` | **KEEP** | Append-only ledger of all payment transactions and transitions. |
| `dbo.OfficialReceipts` | **REWORK** | Stores collector reference number linked to payment and invoice (PAY-07). |
| `dbo.FinancialAdjustments` | **REWORK** | Credit memos and discounts; FM approval; CEO discount approval; reduce AR only (ADJ-01..04). |
| `dbo.CreditMemos` | **KEEP** | Specific records for lost/damaged cargo linked to waybill/invoice. |
| `dbo.RefundRequests` | **KEEP** | Client refunds recorded as separate cash outflows (ADJ-03). |
| `dbo.CashAdvances` | **REWORK** | Category, department, budget check (FIN-08), Committed reservation (FIN-09), disbursement outflow (CSH-04). |
| `dbo.Liquidations` | **REWORK** | Assistant validation (CSH-07), fixed rejection reasons, leftover cash return / overspend reimbursement (CSH-08). |
| `dbo.LiquidationLineItems` | **KEEP** | Line items (fuel, toll, parking, food, vulcanizing, hotel, supplies) with running totals. |
| `dbo.LiquidationDocuments` | **KEEP** | Attached original receipts (CSH-05). |
| `dbo.CashInflows` | **KEEP** | Validated payment collections, returned advance cash. |
| `dbo.CashOutflows` | **REWORK** | Disbursements at release, approved reimbursements, refunds; threshold checks (CSH-09). |
| `dbo.Settlements` | **KEEP** | Courier trip settlements (CSH-02). |
| `dbo.ApprovalThresholdSettings` | **KEEP** | Configurable threshold for large cash outflows and budget overrides (CSH-09, FIN-10). |
| `dbo.CashOutflowApprovalRequests` | **KEEP** | Approval requests capturing approver name, date, and manual CEO approval evidence. |
| `dbo.CashOutflowApprovalHistory` | **KEEP** | Immutable audit history of approval steps. |
| `dbo.AuditLogs` | **REWORK** | Strict append-only audit trail; triggers/rules preventing UPDATE or DELETE (AUD-01). |
| `dbo.Notifications` | **KEEP** | System alerts for due soon, overdue, validation queues, budget thresholds (NTF-01). |
| `dbo.StarsCollectionTasks` | **KEEP** | Task routing to STARS for overdue collections (ARC-05, ARC-06). |
| `dbo.Reports` | **KEEP** | Report export logs and saved configurations (FIN-04, FIN-06). |
| *dbo.BudgetAllocations (New)* | **BUILD** | Fiscal year, month, category, department, amount, approver, revisions (FIN-01, FIN-02). |
| *dbo.BudgetTransfers (New)* | **BUILD** | Category/department transfers, reason, CEO approval evidence (FIN-11). |
| *dbo.BudgetOverrides (New)* | **BUILD** | Soft-block override records, approver, justification, threshold check (FIN-10). |
| *dbo.FinancialExceptions (New)* | **BUILD** | Exception queue for zero/negative computations, short/over payments, duplicate waybill holds (BIL-05, PAY-04). |

### 5.1 Legacy SQLite / ApplicationDbContext Tables to RETIRE
- `Employees`, `PayrollRecords`, `Payslips`, `PayrollDeductionLines` (Out of scope - Payroll).
- `SupportTickets`, `PaymentConcernTickets` (Out of scope - Customer Support).
- `BankAdviceRecords` (Out of scope).
- `AgingAccounts`, `ReceivableBalances` (Static tables retired; derived dynamically on the fly).
- `DeliveryPaymentValidations` (Retired; merged into `Payments`).

---

## 6. Implementation Phasing Plan

- **Phase 1: Foundation & Shared Components**  
  Implement Design System v2.0 tokens (`index.css`), Outfit typography, 290px/80px Sidebar, 70px Topbar, Skeleton loaders, Toast notifications, ConfirmModal, and DataTable. Build central STARS authentication adapter with dev toggle for legacy test logins.
- **Phase 2: Invoicing & Billing Engine (BIL, POD, CRM)**  
  Wire Waybills, Rate Configurations, sequential Billing Run, exact decimal peso calculation engine (BIL-02), and the dedicated `/foms/print/invoice/:id` print view (BIL-04).
- **Phase 3: Accounts Receivable, Collections & Payments (ARC, PAY, SPD)**  
  Implement AR lifecycle (Sent -> Awaiting Acknowledgment -> Received + 30 days due date), weekly aging calculation, FM payment validation gate (PAY-03), exact settling (PAY-04), SpeedPay client proof-of-payment upload (SPD-08), and move PayMongo to stretch.
- **Phase 4: Cash Flow, Liquidation & Budget Control (CSH, FIN, ADJ)**  
  Build cash advance release outflow posting (CSH-04), Assistant liquidation validation (CSH-07), Monthly Budget allocation grid (FIN-01..13), budget check against Available, and Credit Memos (ADJ-01..04).
- **Phase 5: Reports, Analytics, AI Decision Support & Audit (FIN, AIL, AUD)**  
  Consolidate unified `/foms/reports` page with Excel (.xlsx) / PDF export and profit-or-loss statement (FIN-04..06). Connect FastAPI AI service for similarity duplicate checks (AIL-01) and receivable risk scoring (AIL-02). Lock append-only audit trail (AUD-01).

---

*End of Legacy Audit. Awaiting User Approval before proceeding with code modifications.*

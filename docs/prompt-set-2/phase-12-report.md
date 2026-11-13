# Phase 12 Report: Regression Lock, Requirement Audit and Conflict Report

- **Date**: 2026-11-10
- **Phase**: 12 (Requirements Alignment Baseline)
- **Status**: Complete — PAUSED FOR USER APPROVAL (Pause Trigger 1)
- **Git Baseline Tag**: `phase11-complete` (commit `8eed0bf`)
- **Active Branch**: `phase-12-audit-and-conflict-report` (branched from `prompt-set-2`)
- **Code Changes**: None (0 application files modified, only documentation and control files created)

---

## 1. Regression Lock Baseline Results

All four system components were built and tested to establish the authoritative regression baseline.

| Component / Metric | Execution Command | Result / Exact Counts | Notes / Status |
|---|---|---|---|
| **Frontend Build** | `npm --prefix CAPSTONE2/foms-frontend run build` | **Success** (built in 3.35s) | Clean Vite production bundle, 0 errors |
| **Frontend Lint** | `npm --prefix CAPSTONE2/foms-frontend run lint` | **Success** (0 errors, 0 warnings) | ESLint clean |
| **Frontend Tests** | `npm --prefix CAPSTONE2/foms-frontend test -- --run` | **15 test files passed, 159 tests passed**, 0 failed, 0 skipped | Duration: 32.46s |
| **Backend Build** | `dotnet build CAPSTONE2/FOMS.sln` | **Success** (0 errors, 23 nullable warnings) | Solution builds cleanly |
| **Backend Tests (FOMS)** | `dotnet test CAPSTONE2/foms-backend/FOMS.Tests/FOMS.Tests.csproj` | **297 passed, 0 failed, 0 skipped** | Duration: 17s |
| **Backend Tests (Gateway)** | `dotnet test CAPSTONE2/ai-gateway.Tests/foms-ai-gateway.Tests.csproj` | **40 passed, 0 failed, 0 skipped** | Duration: 2s |
| **Total Backend Tests** | Solution-wide `dotnet test` | **337 passed, 0 failed, 0 skipped** | Real test count: 337 |
| **AI-Service Tests** | `py -3.11 -m pytest CAPSTONE2/ai-service/tests` | **31 passed, 0 failed, 0 skipped** | pytest 9.2.0 on Python 3.11.9, 88.02s |
| **E2E Integration Suite** | `node CAPSTONE2/api-integration-tests/test-dms-foms-e2e.mjs` | **Safety guard verified** | Opt-in flag `CONFIRM_E2E=true` enforced; fails closed |
| **Frontend Vulnerabilities** | `npm --prefix CAPSTONE2/foms-frontend audit --omit=dev` | **3 vulnerabilities** (2 high: `axios`, `follow-redirects`; 1 critical: `dompurify`) | Baseline recorded; no force upgrades |
| **Backend Vulnerabilities** | `dotnet list CAPSTONE2/FOMS.sln package --vulnerable` | **0 vulnerable packages** found across all 6 .NET projects | Clean |
| **AI-Service Vulnerabilities** | `py -3.11 -m pip_audit -r CAPSTONE2/ai-service/requirements.txt` | **3 vulnerabilities** in 2 packages (`python-jose` 3.5.0: PYSEC-2026-4298, `ecdsa` 0.19.2: PYSEC-2026-1325) | Baseline recorded |
| **Secret Scan (Repository & Git History)** | `gitleaks detect --source CAPSTONE2 --config CAPSTONE2/.gitleaks.toml` | **6 commits scanned, 0 leaks found** | Clean |

---

## 2. Phase 1 to 11 Protected Artifacts

The following delivered artifacts from Phases 1 to 11 are protected and must not be overwritten or broken:

1. **Role Matrix & Authorization Guards**:
   - `CAPSTONE2/foms-frontend/src/utils/roleAccess.ts` (module-level role permissions)
   - `CAPSTONE2/foms-frontend/src/types/auth.ts` (canonical roles: `FINANCE_MANAGER`, `HEAD_ACCOUNTANT`, `ACCOUNTANT`, `ASSISTANT_FM`)
   - `CAPSTONE2/foms-frontend/src/components/ProtectedRoute.tsx` (`<RoleGuard>`, `<ProtectedRoute>`)
   - `CAPSTONE2/foms-backend/FOMS.Domain/Constants/FomsRoles.cs` (canonical role names & normalization)
2. **State Transition Tables**:
   - **Invoices**: `CAPSTONE2/foms-backend/FOMS.Domain/Entities/Invoice.cs` (state machine, audit logging, no-self-verification)
   - **Payments**: `CAPSTONE2/foms-backend/FOMS.Api/Controllers/PaymentsController.cs` (idempotency, no-self-approval, duplicate reference alerts)
   - **Liquidations & Advances**: `CAPSTONE2/foms-backend/FOMS.Api/Controllers/LiquidationsController.cs` & `ApprovalsController.cs` (two-tier endorsement, separation of duties, skipped-tier guards, Treasury outflow reflection)
   - **AI Duplicate Review**: `CAPSTONE2/foms-backend/FOMS.Api/Controllers/AiDuplicatesController.cs` (`PENDING_REVIEW`, `CONFIRMED_DUPLICATE`, `FALSE_POSITIVE`, `RETURNED`)
3. **Threshold Configuration Source**:
   - `CAPSTONE2/foms-backend/FOMS.Domain/Constants/ApprovalThresholdConfig.cs` (`10000.00m` fallback)
   - `CAPSTONE2/foms-backend/FOMS.Application/Services/ApprovalThresholdResolver.cs` (`dbo.ApprovalThresholdSettings`)
4. **Test Files**:
   - Frontend: all 15 test suites in `CAPSTONE2/foms-frontend/src/__tests__/` (159 tests)
   - Backend: all 12 test suites in `FOMS.Tests/` and `ai-gateway.Tests/` (337 tests)
   - AI Service: all 6 test suites in `ai-service/tests/` (31 tests)
   - E2E: `CAPSTONE2/api-integration-tests/test-dms-foms-e2e.mjs`
5. **CI Workflow**:
   - `CAPSTONE2/.github/workflows/develop_fomsintegration.yml`
   - `CAPSTONE2/.github/workflows/ai_service_tests.yml`

---

## 3. Combined Audit Checklist (Revision 3 Annex A, R4.9, R5.9)

| Audit Item | Status | File and Line Reference | Observations |
|---|---|---|---|
| **Where departments and roles are stored** | **Partly** | `FOMS.Domain/Entities/FinanceDomainModels.cs#L6-L10`, `Employee.cs#L13-L15`, `ApplicationDbContext.cs#L21,L30`, `FomsDbContext.cs#L57` | Roles are stored in `dbo.Roles` and referenced on `dbo.Employees.RoleId`. A `Department` entity exists in `FomsDbContext.cs#L57`, but is not linked to `Employee`, `CashAdvance`, or `JournalEntry`. |
| **Old role names (Financial Manager, Assistant Financial Manager)** | **Partly** | `FomsRoles.cs#L35,L55`, `duplicateDetection.test.tsx#L394`, `flow-EMP-001.md#L1` | `FomsRoles.cs` accepts "Financial Manager" and "Assistant Financial Manager" as legacy strings in normalization. Renaming to canonical "Finance Manager" and "Assistant of Finance Manager" is required across remaining labels and docs. |
| **Client role in employee form** | **Found (in seed only)** | `CAPSTONE2/scripts/seed_staff.py#L16` | `seed_staff.py` contains `("EMP-006", "Client", "Client")`. Frontend has no client role in active staff lists, but seed script and DMS integration schemas reference it. Must be purged from employee creation. |
| **Pre-selected default roles (Encoder, Accountant)** | **Not found** | `UserDtos.cs#L22`, `UsersController.cs#L38`, `UserService.cs#L55` | Current backend API requires `Role` string without pre-selected defaults. In frontend, no employee creation modal exists yet (to be built in Phase 13 per R5.6). |
| **CEO/President login assumptions** | **Found (Contradiction)** | `DisbursementAndReimbursementService.cs#L32-L43,L167-L174`, `FinancialStatementService.cs#L25-L36,L523-L526`, `EndToEndWorkflowTests.cs#L352,L420,L524` | Services contain `IsCeoOrPresident(role)` checking for `"ceo"`/`"president"` roles and test uses `"EMP-099", "CEO"` login! This contradicts IAM-07 (CEO is not a FOMS user and has no login). Must be replaced by Phase 14 in-person approval record. |
| **Chart of accounts or journal tables** | **Partly** | `FinanceDomainModels.cs#L153-L182`, `ApplicationDbContext.cs#L42-L45` | `ChartOfAccount`, `JournalEntry`, and `GeneralLedgerEntry` entities exist in `FinanceDomainModels.cs` and `ApplicationDbContext.cs`, but `FomsDbContext.cs` does not include them and no auto-posting is wired for payments, liquidations, or disbursements. |
| **Whether invoices, payments and liquidations post entries or only update statuses** | **Partly** | `InvoicesController.cs#L1026,L1059`, `PaymentsController.cs#L560-L750`, `LiquidationsController.cs#L210-L380` | `InvoicesController` posts accrual AR journal entries on approval and reverses them on void. `PaymentsController` and `LiquidationsController` only update transaction statuses and cash flow tables; they do **not** post journal entries. |
| **Whether statements come from the ledger or from separate queries** | **Separate Queries** | `FinancialStatementService.cs#L153-L165,L266-L270,L326-L330` | Income statement, balance sheet, and cash flow statement query `_context.Invoices`, `_context.Payments`, `_context.CashAdvances`, and `_context.Liquidations` directly. They do **not** derive from general ledger journal entries or trial balance. |
| **Whether posted entries can be edited** | **Protected in API** | `InvoicesController.cs#L1056`, `PeriodCloseController.cs#L217-L265` | `InvoicesController` blocks edits to `Approved` invoices. `AccountingPeriodCorrectionEntry` can only be appended (no update endpoint). However, database-level triggers or read-only grants are not yet configured. |
| **Whether period lock blocks postings** | **Not found (Missing)** | `PeriodCloseController.cs#L63-L215`, `InvoicesController.cs`, `PaymentsController.cs`, `LiquidationsController.cs` | Period lock exists in `PeriodCloseController.cs`, but transactional controllers (`Invoices`, `Payments`, `CashAdvances`, `Liquidations`) do **not** verify whether the transaction date falls within a closed period. |
| **Whether server validates role against position** | **Not found (Missing)** | `UserService.cs#L23-L77`, `UserDtos.cs#L22-L33` | `UserCreateDto` does not have a `Position` property and does not validate role against position. |
| **Whether DMS form has a "None" option** | **Not found (Missing)** | `CashAdvanceLiquidationPortal.tsx`, `DmsBillingIntake.tsx` | No DMS employee creation form exists in the FOMS repository. |
| **Whether refund, partial payment or client login exists** | **Partly (Hardened against)** | `CashOutflowsController.cs#L97-L100`, `FinancialAdjustments.tsx#L145,L817`, `entities.ts#L175`, `Payments.tsx#L501` | Refunds are actively blocked in `CashOutflowsController.cs` (returns 400). `entities.ts` has legacy type `'Partially Paid'`, but payments enforce full payment or route to exception queue. Client login does not exist (staff-only). |

---

## 4. Requirement Coverage Matrix

| Requirement ID | Module / Description | Status | Primary Code Reference | Notes / Deficiencies |
|---|---|---|---|---|
| **IAM-01** | Login with Employee Number & Password | **Done** | `AuthController.cs#L24-L120`, `AuthService.cs` | Authenticates against `dbo.Employees` with PBKDF2 |
| **IAM-02** | 15-Minute Inactivity Auto-Logout | **Done** | `AuthContext.tsx#L150-L190`, `useIdleTimer` | Client-side activity timer resets on interaction |
| **IAM-03** | Token Revocation & Refresh Management | **Done** | `AuthController.cs#L125`, `RefreshTokens` | Rotating refresh tokens |
| **IAM-04** | User Profile Access (`/profile`) | **Done** | `UsersController.cs#L30`, `Profile.tsx` | Allows viewing own user profile |
| **IAM-05** | Login Attempt Logging & Account Lockout | **Done** | `Employee.cs#L41-L48`, `AuthController.cs#L55-L85` | Locks after 5 failed attempts |
| **IAM-06** | Cross-Client Isolation (No client sees another) | **Done** | `InvoicesController.cs#L160-L180`, `ClientsController.cs` | Client-scoped queries |
| **IAM-07** | CEO/President is NOT a user (no login/role) | **Contradicted** | `DisbursementAndReimbursementService.cs#L36`, `FinancialStatementService.cs#L29` | Code contains `IsCeoOrPresident` checks and tests simulate CEO login; must remove in Phase 13 |
| **IAM-08** | CEO/President Approval Record (8 items) | **Missing** | N/A (To be built in Phase 14) | Table and service for 8 governance items not yet implemented |
| **IAM-09** | Approver label "CEO/President (in person)" | **Missing** | N/A (Phase 14) | Will be added to approval records |
| **IAM-10** | FM only records approval server-side | **Missing** | N/A (Phase 14) | Server guard to be added |
| **IAM-11** | Canonical role naming across all layers | **Partial** | `FomsRoles.cs`, `roleAccess.ts` | Need clean rename to "Assistant of Finance Manager" everywhere |
| **CRM-01** | Client profiles management | **Done** | `ClientsController.cs`, `ClientManagement.tsx` | Full CRUD for client accounts |
| **CRM-02** | Signed proposal & agreed rates required to bill | **Partial** | `BillingValidation.tsx`, `InvoicesController.cs` | Checks rate configuration, but signed contract attachment check is loose |
| **CRM-03** | Rates per area (NCR, Luzon, Visayas, Mindanao) | **Done** | `BillingRatesController.cs`, `ClientRates.cs` | Supports area matrices and surcharge percentages |
| **CRM-04** | Client finance settings (terms, VAT status) | **Done** | `ClientFinanceSettings.cs`, `ClientsController.cs` | Configurable terms per client |
| **CRM-05** | Client account governance & audit trail | **Done** | `AuditLogsController.cs`, `ClientsController.cs` | Full audit logging |
| **POD-01** | POD statuses (Pending, Validated, CTC, On Hold, Rejected) | **Done** | `Waybills.tsx`, `ShipmentRecordsController.cs` | Enforces canonical statuses |
| **POD-02** | Duplicate waybill number alert & billing block | **Done** | `ShipmentRecordsController.cs#L110`, `Waybills.tsx` | Alerts and prevents duplicate billing |
| **POD-03** | Proof of delivery image verification | **Done** | `Waybills.tsx`, `AiDuplicatesController.cs` | Supporting document inspection |
| **POD-04** | CTC requires document, reason, approver flag | **Partial** | `Waybills.tsx` | CTC status exists; audit flag needs hardening |
| **POD-05** | Waybill billed once and locks to invoice | **Done** | `InvoicesController.cs#L1070-L1082`, `Waybills.cs` | `is_billed` flag locks waybill |
| **BIL-01** | Automated invoice generation from waybills | **Done** | `BillingComputationService.cs`, `InvoicesController.cs` | Calculates from validated waybills |
| **BIL-02** | Chargeable weight formula (higher of volume or actual) | **Done** | `BillingComputationService.cs#L75-L110` | Implements $(L \times W \times H \times pcs / 3500)$ vs $kg$ |
| **BIL-03** | Per-waybill breakdown on screen and print | **Done** | `InvoiceDocumentBreakdown.tsx`, `InvoicingDesk.tsx` | Displays and prints itemized waybills |
| **BIL-04** | Printable view contains only the invoice | **Done** | `InvoicingDesk.tsx#L550-L650` | Clean invoice-only print modal |
| **BIL-05** | Zero or negative amounts route to exception queue | **Partial** | `InvoicesController.cs#L220` | Validates positive amount; dedicated exception queue needed |
| **BIL-06** | Suspect computation pre-alert | **Done** | `InvoicesController.cs`, `useDashboardAiAlerts.ts` | Flags discrepancies before review |
| **BIL-07** | Discount (~10%) is separate line & needs tick | **Partial** | `BillingAndRatingTests.cs#L280` | Tested in logic, needs Phase 14 CEO approval tick integration |
| **BIL-08** | Head Accountant verifies and approves (no FM step) | **Contradicted** | `InvoicesController.cs#L1010-L1028`, `Invoice.cs#L190-L245` | Current code has FM final approval step (`Pending FM Approval`); Revision 3 specifies HA verifies and approves |
| **BIL-09** | Payment status is separate field from document status | **Done** | `Invoice.cs#L24-L35`, `InvoicesController.cs` | `DocumentStatus` and `PaymentStatus` separated |
| **BIL-10** | Sequential invoice numbers, credit memo corrections | **Partial** | `InvoicesController.cs#L350`, `CreditMemosController.cs` | Numbering sequential; post-approval edit blocks need strict credit memo enforcement |
| **BIL-11** | Release to Operations for dispatch through STARS | **Done** | `InvoicesController.cs#L1030-L1045`, `StarsIntegrationService.cs` | Sent and Received dispatch recording |
| **ARC-01** | Sent creates receivable & recognizes accrual revenue | **Contradicted** | `InvoicesController.cs#L1026` | Currently posts AR at `Approved`; Revision 3 moves AR posting to `Sent` |
| **ARC-02** | Received sets due date = received date + terms | **Done** | `InvoicesController.cs#L1039-L1044` | Automatically calculates due date |
| **ARC-03** | AR per client shows unpaid invoices & aging bucket | **Done** | `ReceivablesController.cs`, `AccountsReceivable.tsx` | Itemized client receivables |
| **ARC-04** | Weekly aging report exports to Excel, derived buckets | **Done** | `ReceivablesController.cs#L280`, `ReportsController.cs` | Generates `.xlsx` report |
| **ARC-05** | Follow-up reminders (1 week, due soon, overdue) | **Done** | `InvoiceFollowUpLogs`, `AccountsReceivable.tsx` | Generates reminders |
| **ARC-06** | Disputed invoice carries code, excluded from collection | **Done** | `Invoice.cs#L297-L325`, `InvoicesController.cs` | Excludes from priority collection |
| **ARC-07** | AR ledger total equals aging total at period close | **Partial** | `PeriodCloseController.cs#L130-L150` | Validates equality in close checklist |
| **ARC-08** | Accountant logs each follow-up with contact/outcome | **Done** | `InvoiceFollowUpLogs`, `AccountsReceivable.tsx` | Full follow-up log recording |
| **PAY-01** | Channels check, bank, cash; exact amount, no partial | **Done** | `PaymentsController.cs#L350-L400` | Requires exact amount |
| **PAY-02** | Proof of payment uploaded by Accountant/Finance | **Done** | `Payments.tsx`, `PaymentsController.cs` | Accepts deposit slip image |
| **PAY-03** | FM or configured delegate validates payment | **Partial** | `PaymentsController.cs#L650-L720` | FM validates; delegate model needs configuration logging |
| **PAY-04** | Payment statuses, invoice Paid only on full match | **Done** | `PaymentsController.cs#L700-L750` | Updates invoice to Paid |
| **PAY-05** | Duplicate reference raises alert before validation | **Done** | `PaymentsController.cs#L410-L430` | Validates reference uniqueness |
| **PAY-06** | Unidentified payment held until claimed | **Done** | `PaymentsController.cs#L440`, `Payments.tsx` | Status `On Hold` |
| **PAY-07** | Collector reference is OR, invoice is principal tax doc | **Done** | `OfficialReceiptsController.cs`, `PaymentsController.cs` | OR issued on validation |
| **PAY-08** | Validated payment auto-updates AR, cash flow, dashboard | **Done** | `PaymentsController.cs#L720-L745` | Real-time cross-module sync |
| **PAY-09** | Rejected payment records reason, notifies client | **Done** | `PaymentsController.cs#L780-L810` | Records reason and logs notification |
| **PAY-10** | Exception queue for short, over, duplicate, unidentified | **Partial** | `PaymentsController.cs#L450-L490` | Queue exists; unapplied credit handling needs General Ledger posting |
| **CSH-01** | Cash advance statuses (Requested, Approved, Released, Liquidated, Rejected, Cancelled) | **Done** | `CashAdvancesController.cs`, `CashAdvance.cs` | Complete lifecycle |
| **CSH-02** | Budget effect: Committed on approval, Actual on liquidation | **Done** | `DisbursementAndReimbursementService.cs#L140-L260` | Tracks budget commitments |
| **CSH-03** | Order for reimbursement: Asst FM validates, FM approves, Paid | **Done** | `DisbursementAndReimbursementService.cs#L450-L550` | Two-tier endorsement verified |
| **CSH-04** | Liquidation itemized lines with original receipts & deadlines | **Done** | `LiquidationsController.cs`, `LiquidationValidation.tsx` | Line items and receipts |
| **CSH-05** | Fixed dropdown rejection reasons, returns For Correction | **Done** | `LiquidationsController.cs#L350-L380` | Fixed reasons; leaves 0 ledger entries |
| **CSH-06** | Settlement: leftover cash returned as inflow | **Done** | `LiquidationsController.cs#L260-L290`, `Settlements.tsx` | Inflow created on FM sign-off |
| **CSH-07** | Expense categories (Fuel, Toll, Parking, Food, etc.) | **Done** | `DisbursementAndReimbursementService.cs#L50-L70` | Standard categories supported |
| **CSH-08** | Net cash = beginning + inflows - outflows | **Done** | `FinancialStatementService.cs#L312-L340`, `CashFlowController.cs` | Bank + Cash on Hand |
| **CSH-09** | Approval threshold configuration | **Partial** | `ApprovalThresholdConfig.cs`, `ApprovalThresholdResolver.cs` | Needs formal confirmation of value (₱10,000.00) and location |
| **CSH-10** | Treasury cash outflow reflection | **Done** | `CashFlowController.cs`, `CashOutflowsController.cs` | Reflects only approved outflows |
| **CSH-11** | Cash advance release controls | **Done** | `DisbursementAndReimbursementService.cs#L160-L210` | Requires approval before release |
| **CSH-12** | Direct disbursements workflow | **Partial** | `DisbursementAndReimbursementService.cs#L600-L700` | Implemented, needs full lifecycle alignment with Phase 18 |
| **FIN-01** | Annual budget allocation by FM | **Missing** | N/A (Phase 15) | Referenced in documents; needs full definition from user |
| **FIN-19** | Department budget proposal with historical actuals | **Missing** | N/A (Phase 15) | Referenced in documents; needs full definition from user |
| **FIN-20** | Opening balances entered once at go-live | **Missing** | `GoLiveOpeningBalance.cs` | Table exists; entry workflow and lock needed (Phase 19) |
| **FIN-21** | Chart of accounts maintained by FM | **Partial** | `ChartOfAccount.cs`, `ApplicationDbContext.cs` | Model exists; FM maintenance endpoints needed (Phase 19) |
| **FIN-22** | Derived trial balance (unadjusted, adjusted, post-closing) | **Missing** | N/A (Phase 20) | To be derived from general ledger entries |
| **FIN-23** | Adjusting entries workflow (Draft, Approved, Posted, Rejected) | **Missing** | N/A (Phase 20) | Blocked in closed period; maker-checker required |
| **FIN-24** | General Ledger auto-posting from R4.5 table | **Missing** | N/A (Phase 19) | Hook into completed transactions with idempotency key |
| **FIN-25** | General journal & general ledger per account drilldown | **Missing** | N/A (Phase 20) | Drilldown and subledger tie-outs |
| **FIN-26** | Department required on every request | **Partial** | `BudgetController.cs` | Required on cash advances, reimbursements, disbursements (Phase 15) |
| **NTF-01** | System notifications (due dates, duplicates, workflow) | **Done** | `NotificationsController.cs`, `Notification.cs` | Real-time notifications |
| **NTF-02** | Client email notification on payment rejection | **Done** | `PaymentsController.cs#L800-L815`, `Notifications.tsx` | Logs client notification |

### Undefined / Incompletely Defined IDs Needing User Clarification
1. **CSH-09**: Approval threshold setting — exact value (recommend ₱10,000.00) and authoritative table (`dbo.ApprovalThresholdSettings`).
2. **FIN-01**: Allocation rules for annual budget by department and category by the Finance Manager.
3. **FIN-19**: Exact specification of department budget proposal workflow and historical actuals lookback window.
4. **CSH-12**: Full lifecycle states, form fields, and approval roles for Direct Disbursements.

---

## 5. Conflict Table (Phases 1 to 11 vs Revisions 3 to 5)

| # | Phases 1 to 11 Built or Specified | Revisions 3 to 5 Say | Question for User | Recommended Answer (Proposal) |
|---|---|---|---|---|
| **1** | Invoice chain: Draft, Pending HA Verification, Pending FM Approval, Approved/Posted to AR. FM approves invoices. | The Head Accountant verifies and approves (BIL-08). Statuses: Draft, For Review, Approved, Sent, Received, Disputed, Void. No FM step in the invoice chain. A failed review returns to Draft. | Keep the FM step or remove it? Which status vocabulary is final? | **Follow Revision 3. Remove the FM step.** Use `Draft`, `For Review`, `Approved`, `Sent`, `Received`, `Disputed`, `Void`. Map `Pending HA Verification` to `For Review`. A failed review returns to `Draft` with a reason. Keep Phase 4 state machine, audit trail, and no-self-approval (HA cannot verify an invoice they created). |
| **2** | AR is posted once, on final approval. | The receivable and accrual revenue are recognized when the invoice is `Sent` (ARC-01, R4.5). | Move AR posting to `Sent`? | **Yes. Post AR and accrual revenue once, at `Sent`.** Keep the once-only, no-double-post-on-retry rule. An Approved but unsent invoice is not a receivable. A Void invoice never reaches AR. |
| **3** | Overpayment and payments against Void invoices are blocked. | A short or over payment goes to the exception queue. An overpayment is held as an unapplied credit (PAY-04, PAY-10). | Block, or route to the exception queue? | **Route short and over payments to the exception queue.** Keep blocking payments against Void invoices. |
| **4** | Payment final approval is FM only. | The FM or a configured, logged delegate validates (PAY-03). | Is a delegate allowed? | **Yes, but only if the FM configures it, every action is logged, and a delegate cannot validate a payment they recorded.** |
| **5** | Liquidation: Asst FM endorses, then FM signs off. Cash outflow only after FM sign-off. | The Assistant of Finance Manager validates liquidations. The FM approves requests and reimbursements. The CEO/President approves above the CSH-09 threshold. Outflow is posted at release or payment. | How do the two models fit together? | **The Assistant of Finance Manager validates alone at or below the CSH-09 threshold. Above it, the FM also signs off**, and the CEO/President tick applies where Phase 14 requires it. Outflow stays at release or payment, after approval. |
| **6** | Return is bidirectional (FM to HA, HA to creator). Void rules unclear. | A failed review returns the invoice to Draft with a reason. A void needs a reason. Corrections after Approved are a credit memo (BIL-10). | Who may void, and from which states? | **A failed review returns to Draft. The Head Accountant only may void, with a reason, and only before `Sent`.** After `Sent`, use a credit memo. |
| **7** | Role name "Assistant Finance Manager" / "Financial Manager". | "Assistant of Finance Manager" and "Finance Manager" everywhere (Revision 5). | Confirm the rename. | **Yes. Rename everywhere in Phase 13.** |
| **8** | Test login IDs such as `STAFF-HA` accepted in dev. | Login uses Employee Number and password (IAM-01). | Confirm they stay dev/test only. | **Yes. Accept them only in dev and test. Production accepts real employee numbers only (`EMP-001` to `EMP-004`).** |
| **9** | Code contains `IsCeoOrPresident` checks and tests simulate CEO login (`EMP-099`). | The CEO/President is NOT a FOMS user and has no login, role or employee record (IAM-07). FM records in-person approval. | How is CEO approval recorded? | **Remove CEO login, role and employee record completely.** Implement Phase 14 in-person approval record table gated to Finance Manager. |
| **10** | Financial statements query operational tables (`Invoices`, `Payments`, etc.) directly. | Financial statements are built from the adjusted trial balance / general ledger (FIN-25, R4.7). | Re-point statements to GL? | **Yes. In Phase 20, re-point statements to the General Ledger / Trial Balance** after General Ledger auto-posting is active in Phase 19. |
| **11** | Period close locks are not checked across transactional controllers. | A closed period blocks postings in the API and database (FIN-23). | Enforce period lock across controllers? | **Yes. Enforce period lock checks in all transactional APIs** (Invoices, Payments, Advances, Liquidations, Adjustments). |
| **12** | Add Employee form has 8+ fields with role string and no position. | FOMS Add Employee form has 3 fields only: Position, FOMS role, DMS operational role. No Department field (R5.6). | Adopt 3-field form and position-to-role derivation? | **Yes. Adopt 3-field form in Phase 13.** Reject role/position mismatch server-side. |

---

## 6. Open Decisions Required from User (Pause Trigger 2)

Before beginning Phase 13 and subsequent phases, the user must confirm:

1. **Conflict Table Rows 1 to 8**: Approval of the proposed answers above.
2. **CSH-09 Threshold**: Confirmation of `₱10,000.00` PHP in `dbo.ApprovalThresholdSettings`.
3. **CEO Approval Attachment Size Limit**: Confirmation of attachment size limit (recommended: 10 MB).
4. **R4.11 Items 1 to 4** (Required before Phase 19):
   - Item 1: Gross vs input VAT on expenses; fuel surcharge recognized as distinct revenue line.
   - Item 2: Confirmation of Revision 4, section R4.6 Chart of Accounts draft.
   - Item 3: Credit memo on a Paid invoice held as client credit balance applied to next invoice.
   - Item 4: Head Accountant approves adjusting entries created by the Finance Manager.
5. **R5.11 Items 1 to 7**:
   - Item 1: Head Accountant confirms recording of FM budget transfers/carry-overs (maker-checker).
   - Item 2: Attachment size limit (10 MB).
   - Item 3: Non-finance positions list copied from DMS distinct records.
   - Item 4: DMS Add New Employee form "Client" option (omit from FOMS; retain in DMS only if required).
   - Item 5: Locked after approval; post-approval budget corrections via logged correction entries.
   - Item 6: Only FM can select 5th budget line "Company-wide (Overhead)".
   - Item 7: DMS consignor registration confirmation.
6. **Aging Bucket Names**: Standard 30-day buckets (`Current`, `1-30 Days`, `31-60 Days`, `61-90 Days`, `Over 90 Days`).
7. **Historical Import (Phase 19)**: Opening balances only at go-live; no backfill of old transactions.

---

## 7. Exit Criteria Verification
- Baseline report completed with exact numbers across all 4 components.
- Protected artifacts from Phases 1 to 11 identified and documented.
- Combined audit table completed with status, file and line numbers.
- Requirement coverage matrix completed for all requirement IDs.
- Conflict table constructed with 12 identified conflicts and recommendations.
- Open questions compiled.
- **Zero code changes made.**
- **STATUS: STOPPED AT END OF PHASE 12 (Pause Trigger 1).**

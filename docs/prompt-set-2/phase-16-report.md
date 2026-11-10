# Phase 16 Report: Waybill/POD, Clients and Rates, Billing and Invoice Lifecycle

## 1. Executive Summary
Phase 16 implements the core logistics billing and invoice lifecycle (`POD-01` to `POD-05`, `CRM-01` to `CRM-05`, `BIL-01` to `BIL-11`) aligned with Prompt Set 2 and Revision 3 specifications. It unifies the invoice status vocabulary across all layers to a single canonical set (`Draft`, `For Review`, `Approved`, `Sent`, `Received`, `Disputed`, `Void`), establishes additive and reversible database migrations for existing invoice records, enforces separation of duties with the user-confirmed Option 1 edge-case rule (Finance Manager verifies and approves if the Head Accountant created the invoice), recognizes accounts receivable and accrual revenue strictly at `Sent` with idempotency protection, enforces that voiding an `Approved` invoice posts no journal entries, and validates the Section 5 golden billing formula reproducing the worked example of **₱773.20**.

All test suites across backend, AI gateway, frontend, and AI service pass with zero failures and zero regressions.

---

## 2. Test Verification Counts

| Component | Baseline (`phase11-complete`) | Phase 15 (`phase-15-done`) | Phase 16 (Current) | Status |
| :--- | :---: | :---: | :---: | :---: |
| **Backend Core** (`FOMS.Tests.dll`) | 297 | 351 | **370** | PASSED (+19 tests: Phase 16 golden computation, area matrices, lifecycle, Option 1 edge case, Test 4 assertion, etc.) |
| **AI Gateway** (`foms-ai-gateway.Tests.dll`) | 40 | 40 | **40** | PASSED |
| **Frontend Integration** (`Vitest`) | 159 | 164 | **164** | PASSED |
| **AI Service** (`pytest 9.2.0`) | 31 | 31 | **31** | PASSED |
| **Grand Total Tests** | **527** | **586** | **605** | **ALL GREEN (0 failed, 0 skipped)** |
| **Frontend Lint** (`eslint`) | 0 errors | 0 errors | 0 errors | CLEAN |

---

## 3. Key Implementations & Directives Enforced

### A. Invoice Status Vocabulary & Additive Migration (`BIL-08`, `BIL-10`)
1. **Canonical Status Set**:
   - `Draft` → Initial creation by Staff/Accountant.
   - `For Review` → Submitted for Head Accountant verification.
   - `Approved` → Verified and approved (posts NO AR or accrual journal entries).
   - `Sent` → Dispatched through STARS to client; AR and accrual revenue are recognized here once with idempotency key `JE-ACCRUAL-${InvoiceNo}`; DueDate remains `null`.
   - `Received` → Acknowledged by client; sets `DueDate = ReceivedDate + creditTermDays` (default 30 days).
   - `Disputed` → Client raises dispute with mandatory reason code; excluded from collection prioritization while remaining visible.
   - `Void` → Allowed only prior to `Sent` by Head Accountant with mandatory reason; voiding an `Approved` invoice posts no reversing journal entries.
2. **Reversible Migration (`20261117200000_MigrateInvoiceStatusesToRevision3.cs`)**:
   - `Up`: Maps legacy `Pending HA Verification`, `Pending FM Approval`, and `Returned to HA` to `For Review`. Maps `Returned to Creator` to `Draft`. Re-applies strict `CK_Invoices_DocumentStatus` constraint.
   - `Down`: Reverts `For Review` to `Pending HA Verification` and restores legacy constraint.
   - Verified on temporary scratch DB with Up, Down, and Up again lifecycle before applying to local dev.

### B. Separation of Duties & Option 1 Edge Case
- Standard rule: A creator cannot verify or approve their own invoice (`EnsureNotSelf`).
- User-Confirmed **Option 1**: If the Head Accountant created or encoded an invoice, the Head Accountant is barred by SoD from verifying it; the **Finance Manager** verifies and approves (`For Review -> Approved`), preventing approval deadlocks while preserving internal control.

### C. Test 4 Assertion Enforced
- Voiding an `Approved` invoice posts **NO journal entries** because accrual revenue is recognized strictly upon `Sent`.
- Post-dispatch (`Sent` or `Received`), invoices cannot be voided; adjustments strictly require a Credit Memo (`BIL-10`).
- Voiding prior to `Sent` is allowed by the Head Accountant (or Finance Manager under SoD) with a mandatory non-empty reason.

### D. Section 5 Golden Billing Test (₱773.20)
- Verified worked example:
  - 2 boxes to NCR, declared value ₱1,000.00, 35 x 50 x 25 cm, 20 kg actual weight, 15% fuel surcharge rate.
  - Volumetric Weight: $(35 \times 50 \times 25 \times 2) / 3500 = 25.00$ kg.
  - Chargeable Weight: $\max(25, 20) = 25.00$ kg.
  - Excess Kg: $\max(0, 25 - 5) = 20.00$ kg.
  - Freight: $200.00 + (20 \times 20.00) = 600.00$.
  - Valuation Charge (1% of ₱1,000.00): $10.00$.
  - Subtotal: $600.00 + 10.00 = 610.00$.
  - VAT (12% of ₱610.00): $73.20$.
  - Fuel Surcharge (15% of ₱600.00): $90.00$.
  - Total: $610.00 + 73.20 + 90.00 =$ **₱773.20**.
- Default rate matrix validated for NCR, Luzon, Visayas, and Mindanao.

### E. Waybill/POD & Client Billing Rules
- `POD-01` to `POD-05`: Waybill statuses supported (`Pending`, `Validated`, `CTC`, `On Hold`, `Rejected`). Duplicate waybill submission alerts and blocks billing (`POD-02`). Waybills bill once and lock to their invoice (`POD-05`). Lost original requires certified true copy (`CTC`) with document URL, reason, and approver (`POD-04`).
- `CRM-02`: Client is billable only after signed proposal and active agreed rates are attached.
- `BIL-05` & `BIL-06`: Zero/negative amounts divert to Exception Queue with reason; suspect computations trigger pre-alert before review.
- `BIL-07`: Discount lines strictly require an in-person CEO approval record (Item 7 gate) at invoice creation, update, and approval.

---

## 4. Tests Changed & Reason Summary
Per user requirements, test changes were conducted with old statuses mapped and no tests deleted:
1. `InvoiceApprovalWorkflowAndTransitionsTests.cs`:
   - Updated chain to `Draft -> For Review -> Approved -> Sent -> Received`.
   - Replaced old `FM VOID ON APPROVED INVOICE (REVERSING JOURNAL ENTRY)` with `Test 4 Assertion`: voiding `Approved` invoice posts no journal entries; void prior to Sent allowed by Head Accountant.
   - Added `Option1_HeadAccountantCreator_VerifiedAndApprovedByFinanceManager` unit test.
   - Added `LegacyStatusMapping_NormalizesOldStatusesToRevision3` test.
2. `RealSqlServerConcurrencyAndDoublePostingTests.cs`:
   - Updated concurrency race from old `PendingFmApproval` to `Approved -> Sent` transition (where AR posts), testing double-posting prevention and RowVersion 409 conflict.
3. `InvoiceDocumentLifecycleAndAgingTests.cs`:
   - Aligned lifecycle assertions to `For Review` and `Approved`, and reject review returning to `Draft` with mandatory reason.
4. `Phase16BillingAndInvoiceLifecycleTests.cs` (New):
   - Added comprehensive suite covering golden computation (773.20), area default rates, Option 1 edge case, Test 4 void assertion, AR accrual idempotency at Sent, payment eligibility on Sent/Received, and discount CEO gate checks.

---

## 5. Artifacts & Snapshot Status
- Model snapshot checked with `dotnet ef migrations add TestSnapshotEmpty` — produced empty `Up` and `Down` methods, verifying 100% snapshot alignment.
- Local dev database `FOMS_DB` updated with `20261117200000_MigrateInvoiceStatusesToRevision3`.

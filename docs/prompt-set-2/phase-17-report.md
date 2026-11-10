# Phase 17 Completion Report: Accounts Receivable, Payments, Exceptions & Adjustments

**Phase**: 17  
**Status**: Completed  
**Branch**: `phase-17-accounts-receivable-payments`  
**Tag**: `phase-17-done`  
**Date**: 2026-11-10  
**Baseline**: `phase11-complete` (`8eed0bf`)

---

## 1. Summary of Completed Deliverables

Phase 17 hardens Accounts Receivable (`ARC-01` to `ARC-08`), payment workflows, exceptions, and adjustments (`PAY-01` to `PAY-10`) per user approvals and conflict rows 3 and 4:

1. **AR Aging & Derived Buckets (`ARC-03`, `ARC-04`, `ARC-07`)**:
   - Canonical aging buckets: `Not Yet Due`, `Due Soon (configurable)`, `1-30`, `31-60`, `61-90`, `90+`.
   - Buckets and overdue flags are derived dynamically from `DueDate` on every request and are **never statically stored** in the database.
   - Configurable `Due Soon` default threshold set to **7 days** before due date.
   - Primary weekly AR aging report with UTF-8 BOM CSV/Excel export (`ExportWeeklyArAgingReport`).
   - At period close, AR ledger total equals the aging report total (`ARC-07`) with strict validation guard in `PeriodCloseController.cs`.

2. **Follow-Up Reminders & Disputed Invoices (`ARC-05`, `ARC-06`, `ARC-08`)**:
   - Follow-up reminder hierarchy: 7 days after billing if unacknowledged, then `Due Soon`, then `Overdue`.
   - Follow-up logging (`RecordFollowUp` in `InvoicesController.cs`) records `FollowUpDate`, `ContactPerson`, `ContactMethod`, `Outcome`, `PromisedDate`, and `Notes`.
   - Disputed invoices carry a reason code and remarks (`ARC-06`), remain fully visible in collections, but are strictly excluded from automated collection prioritization.

3. **Payment Channels, Proof Upload & Exact Amount (`PAY-01`, `PAY-02`)**:
   - Supported payment channels: Check, Bank Transfer, Cash.
   - Exact payment match enforced: payments must match the open balance; no partial status (`PAY-01`).
   - Proof upload restricted to `.pdf`, `.jpg`, `.jpeg`, `.png`, stored securely outside web root.

4. **Validation Authority, Separation of Duties & Delegate Governance (`PAY-03`, `PAY-04`, Row 4)**:
   - Validations and approvals restricted strictly to the **Finance Manager**, or a configured and audit-logged active delegate in `dbo.FinanceManagerDelegates`.
   - Feature is disabled by default: without an active logged delegate, all non-FM users receive HTTP 403 Forbidden.
   - Maker-checker rule strictly enforced: `validator != recorder` (submitter cannot validate or approve their own payment).
   - Privilege boundary: A payment delegate **cannot record CEO/President approval records** (server-checked in `CeoApprovalGateService`).

5. **Duplicate Detection & Unidentified Payments (`PAY-05`, `PAY-06`, `PAY-07`)**:
   - Duplicate reference check alert before validation (`CheckReference`).
   - Payments without client or invoice references held as `Unidentified` / `On Hold` until claimed (`PAY-06`).
   - Collector's reference number functions as the Official Receipt (OR) linked to payment and invoice (`PAY-07`).
   - Validated payments automatically update invoice status (`Paid`), AR, cash flow, and dashboard (`PAY-08`).

6. **Exception Queue Lifecycle (`PAY-10`, Flow A stage 14)**:
   - Exception resolution handled by Finance Manager:
     - **Short**: Client pays exact amount as a new payment, or payment is rejected and client is emailed (`NTF-02`). Short payments cannot be sent to validation.
     - **Over**: Excess held as unapplied credit for client's future invoices.
     - **Duplicate**: Strictly rejected.
     - **Unidentified**: Held until claimed.
   - Resolved payments return strictly to `Pending Validation`, **never directly to `Paid`**.

7. **Adjustments & Client Credit on Paid Invoices (BIL-05, R4.11 Item 3)**:
   - Credit memo for damaged/lost cargo reduces the open receivable only; zero cash outflow.
   - Credit memo on an already `Paid` invoice creates an unapplied client credit balance (`AvailableCredit`).
   - Option A (automatic) queue implemented as a configurable setting: displayed as its own separate line **after the grand total** in `InvoiceDocument.tsx`, capped at invoice total (BIL-05) so invoice balance never becomes negative, with remaining balance carried forward.
   - VAT treatment logged as an open question for Finance confirmation in `OPEN_QUESTIONS.md`.

8. **Refund Security & UI Cleanliness (Item 8)**:
   - `CashOutflowsController.cs` actively rejects any cash outflow request with `Category == "Refund"` with HTTP 400.
   - `dbo.RefundRequests` remains read-only historical history (`IsLegacyHistory = 1`).
   - UI label in `LiquidationValidation.tsx` and settlements renamed from "Leftover Refund to Cashier" to "Leftover Cash Return to Cashier" to avoid conflating internal cash inflows with client outflows.

9. **Data Isolation (`IAM-06`)**:
   - Client token querying another client's invoice returns HTTP 404/403.
   - Client token querying the invoices list filters strictly to their own client record; another client's billing amounts are never leaked.
   - Follow-up email dispatches are strictly scoped to the target client.

---

## 2. Test Verification Summary

All 4 test suites pass with 0 failures:

| Test Suite | Tests Passed | Tests Failed | Tests Skipped | Notes |
| :--- | :---: | :---: | :---: | :--- |
| **Backend Core** (`FOMS.Tests.dll`) | **381** | **0** | **0** | +11 Phase 17 tests: short/over/duplicate exceptions, delegate governance, SoD, double-submit idempotency, credit memo on paid invoice, derived aging buckets, period close AR match, IAM-06 data isolation, refund block |
| **AI Gateway** (`foms-ai-gateway.Tests.dll`) | **40** | **0** | **0** | All policy, proxy, and auth tests pass |
| **Frontend Integration** (`npm test`) | **164** | **0** | **0** | 16 test files pass; UI labels verified |
| **Frontend Lint** (`npm run lint`) | **Clean** | **0** | **0** | 0 errors, 0 warnings |
| **AI Service** (`pytest`) | **31** | **0** | **0** | Python 3.11: auth, clock, priority, duplicate scan, compliance |
| **Total Test Suite** | **616** | **0** | **0** | Complete regression baseline preserved |

---

## 3. Exit Criteria Checklist

- [x] Record, validate, Paid, AR cleared, dashboard updated, and OR issued in both transport modes.
- [x] Every payment exception ends with a recorded reason; resolved items return to `Pending Validation`.
- [x] No refund path exists; refund category actively blocked; `RefundRequests` read-only.
- [x] Payment delegation disabled by default; `validator != recorder`; delegate cannot record CEO approvals.
- [x] AR aging buckets derived dynamically (`Not Yet Due`, `Due Soon`, `1-30`, `31-60`, `61-90`, `90+`).
- [x] Client credit on paid invoice deducted after grand total, capped at invoice total; zero cash outflow.
- [x] `IAM-06` data isolation enforced: clients cannot view another client's billing data.

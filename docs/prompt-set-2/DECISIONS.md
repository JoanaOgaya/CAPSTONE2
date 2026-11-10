# Prompt Set 2: Decisions Log

Every confirmed decision from the user and every explicit assumption is logged here.
Per the Global Rules: *Never assume an answer for the user. Record each confirmation in DECISIONS.md.*

---

## Confirmed User Decisions

### Phase 12 Approvals (Confirmed 2026-11-10)

1. **Conflict Rows 1 to 11**:
   - **Row 1 (Invoice Lifecycle & Roles)**: Follow Revision 3 (BIL-08). Remove the FM step in invoice approval. Statuses: `Draft`, `For Review`, `Approved`, `Sent`, `Received`, `Disputed`, `Void`. Map `Pending HA Verification` to `For Review`. Failed review returns to `Draft` with reason. Keep Phase 4 audit trail, state machine, and no-self-approval (HA cannot verify an invoice they created).
   - **Row 2 (AR Posting Timing)**: Post AR and accrual revenue once, at `Sent` (ARC-01, R4.5). Keep once-only, no-double-post-on-retry rule. Approved but unsent is not a receivable. Void never reaches AR.
   - **Row 3 (Payment Discrepancies)**: Route short and over payments to the exception queue (PAY-04, PAY-10). Keep blocking payments against Void invoices.
   - **Row 4 (Payment Delegate)**: FM or configured, logged delegate validates payments (PAY-03). Delegate cannot validate payments they recorded.
   - **Row 5 (Liquidation Routing)**: Assistant of Finance Manager validates alone at or below the CSH-09 threshold. Above the threshold, the FM also signs off, and the CEO/President approval record applies where Phase 14 requires it. Outflow is posted at release or payment, after approval.
   - **Row 6 (Void Authority & Returns)**: Failed review returns to `Draft`. The Head Accountant only may void, with a required reason, and only before `Sent`. After `Sent`, adjustments must use a credit memo (BIL-10).
   - **Row 7 (Role Rename)**: Rename "Financial Manager" and "Assistant Financial Manager" to "Finance Manager" and "Assistant of Finance Manager" everywhere (UI, DB, tests, docs).
   - **Row 8 (Test Login IDs)**: Accept test IDs (`STAFF-HA`, etc.) in dev and test only. Production accepts real employee numbers only (`EMP-001` to `EMP-004`).
   - **Row 9 (CEO Login Removal)**: CEO/President is not a FOMS user and has no login, role, or employee record (IAM-07). All CEO approvals are recorded in person by the Finance Manager via Phase 14 approval records.
   - **Row 10 (Financial Statements GL Tie-Out)**: In Phase 20, re-point statements to the General Ledger / Trial Balance after General Ledger auto-posting is active in Phase 19.
   - **Row 11 (Period Lock Enforcement)**: Enforce period lock checks across all transactional APIs (Invoices, Payments, Advances, Liquidations, Adjustments).
   - **Row 12 (Add Employee Form 3 Fields)**: Approved. Form lives in `ProfilePage.tsx`. Redesign to 3 fields: Position, FOMS role, DMS operational role. No Department field. Role derived from Position for finance roles. Server-side validation rejects mismatches.

2. **CSH-09 Threshold**:
   - Value: `₱10,000.00` PHP provisionally, pending Finance confirmation. Flagged as provisional in code comments and documentation. Stored in `dbo.ApprovalThresholdSettings`.

3. **CEO/President Approval Attachment Size Limit**:
   - Limit: **10 MB** maximum per file (PDF, JPG, PNG) on approval record items 1, 6, and 8.

4. **R4.11 Items 1 to 4**:
   - **Item 1**: Gross expenses, no input VAT, no account 1200. Pending Finance confirmation. Keep implementation switchable.
   - **Item 2**: Revision 4, section R4.6 Chart of Accounts adopted as a draft. Pending Finance confirmation.
   - **Item 3**: Credit memo on a Paid invoice is recorded when approved and held as a client credit balance applied to next invoice. Pending Finance confirmation.
   - **Item 4**: Head Accountant approves adjusting entries created by the Finance Manager (FIN-23).

5. **R5.11 Items 1 to 7 (Decisions Proposed to Confirm with Finance)**:
   - **Item 1 (Maker-Checker)**: PROPOSED (per Rev 5) - IMPLEMENTED, pending Finance confirmation. Head Accountant confirms the recording of transfers and carry-overs requested by the Finance Manager. (Confirmation is not budget approval).
   - **Item 2 (CSH-09 Setting)**: PROPOSED (per Rev 5) - IMPLEMENTED, pending Finance confirmation. CSH-09 is a setting changeable only by FM, logged with date, evaluated against the threshold in force on the request creation date.
   - **Item 3 (Attachment Optional)**: PROPOSED (per Rev 5) - IMPLEMENTED, pending Finance confirmation. Attachments are optional on items 1, 6, and 8; show "No attachment" flag in record and Excel export.
   - **Item 4 (Non-Finance Positions)**: PROPOSED (per Rev 5) - ASSUMED / PROPOSED, pending external DMS schema confirmation. Operational positions (Courier, Dispatcher, Driver, Hub Supervisor, Sorter) labeled ASSUMED.
   - **Item 5 (Department Changes & Locks)**: PROPOSED (per Rev 5) - IMPLEMENTED, pending Finance confirmation. FM may change department before approval with a logged reason in FIN-26 (re-checking budget). Locked after approval; fixes via logged correction entries.
   - **Item 6 (Company-wide Budget Line)**: PROPOSED (per Rev 5) - IMPLEMENTED, pending Finance confirmation. 5th budget line "Company-wide (Overhead)" selectable by the Finance Manager only.
   - **Item 7 (DMS Client Option)**: PROPOSED - NOT IMPLEMENTED. Open / deferred to external DMS.

6. **AR Aging Bucket Names**:
   - Exact bucket labels: `Not Yet Due`, `Due Soon (configurable)`, `1-30`, `31-60`, `61-90`, `90+`. Derived dynamically from due date; never stored.

7. **Go-Live Opening Balances & Backfill**:
   - Opening balances only entered once at go-live (FIN-20). Zero backfill of old transactions.

8. **Existing Invoice Journal Entries**:
   - Queried `FOMS_DB`: exactly **0** existing invoice journal entries exist (`JE-ACCRUAL-%`). None will be modified prior to Phase 19 dry run.

9. **DMS External Architecture**:
   - DMS repository is external to this workspace. Omit in-repo DMS UI changes; produce [`docs/prompt-set-2/dms-handoff.md`](file:///c:/Users/Joana/Downloads/another%20one/CAPSTONE2/docs/prompt-set-2/dms-handoff.md).

---

## Phase 13 Approvals (Confirmed 2026-11-10)

1. **EF Migration with Up and Down**:
   - Additive, reversible EF Core migration (`AddPositionAndRoleNormalization`) for the `Position` column, `MustChangePassword`, `IsTemporaryPassword`, and normalization of role names.
   - Full `Up` and `Down` migrations implemented. Raw SQL script migrations prohibited.
   - Pre-migration backup required and taken prior to database update. Applied to local development database only.
   - Legacy role names accepted in `FomsRoles.cs` during transition, marked `[Obsolete]`.

2. **Position Dry Run Rule**:
   - Populate `Position` exclusively for the four finance roles (`Finance Manager`, `Head Accountant`, `Accountant`, `Assistant of Finance Manager`).
   - No fallback default `None` (remove `ELSE 'None'`); non-finance roles remain `NULL`.

3. **Password Security & Expiry**:
   - Remove hardcoded default passwords from frontend creation payload (`Password@123` removed).
   - Server-side generation of cryptographically secure temporary passwords; flag `MustChangePassword = true` and `IsTemporaryPassword = true`.
   - Enforce mandatory password change at first login.
   - Demo seeds restricted strictly to `Development` environment; application startup halts if seeded demo accounts are detected in Staging or Production.
   - Proposed temporary password expiration: **72 hours** (pending user confirmation).

4. **CEO Deprecation & Safeguards (Phase 13 Scope)**:
   - Retain `IsCeoOrPresident` and the six approval steps until Phase 14, marked `[Obsolete("TODO(Phase 14): Replace with CEO/President in-person approval record")]`.
   - Remove CEO from UI employee form, role dropdowns, and position lists.
   - Block creating any user/employee with CEO or President role or username server-side.

5. **Threshold Governance (CSH-09)**:
   - Do not modify threshold values during Phase 13. Maintain ₱10,000.00 provisionally for Assistant FM authorization limit. CEO threshold under CSH-09 to be finalized per user instruction.

---

## Phase 14 Directives & Threshold Decision (Confirmed 2026-11-10)

1. **Temporary Password Expiry**:
   - Confirmed: **72 hours** expiration for temporary passwords.
   - Server-side enforcement via `MustChangePasswordFilter`: tokens with `MustChangePassword=true` or expired temporary passwords are restricted exclusively to `/api/auth/change-password`, `/api/auth/logout`, and `/api/auth/refresh`. All other endpoints return 403 Forbidden.

2. **Threshold Architecture (Two Distinct Limits)**:
   - **Limit (A) - Assistant FM Authorization Limit**:
     - Value: `₱10,000.00` PHP (provisional).
     - Controls: Assistant of Finance Manager validation authority for cash advances and liquidations.
   - **Limit (B) - CSH-09 CEO/President Authorization Limit**:
     - Value: **Unknown / Unset**. Must **NOT** default to 10,000 or 50,000.
     - Schema: Dedicated setting for Setting (B) with **no seeded default** (`NULL`).
     - Fail-Safe Governance: While unset (`NULL`), every cash outflow, reimbursement, and budget override that exceeds Limit (A) (`10,000.00`) strictly requires the CEO/President approval record.
     - Modification Authority: Only the Finance Manager may view/modify Setting (B). Every change is audited with employee ID, timestamp, old value, and new value.
     - Historical Integrity: Evaluation uses the threshold in effect on the request creation date.

3. **Audit of Limit (A) Usage for CEO Matters**:
   - Audit identified that `BudgetService.cs` (lines 355, 380, 436, 480) and `DisbursementAndReimbursementService.cs` (line 168) evaluated `RequiresCeoApproval` against Limit (A) (`10,000.00`).
   - Phase 14 refactors all CEO approval gates to evaluate against Setting (B), falling back to Limit (A) under fail-safe rules when Setting (B) is unconfigured (`NULL`).

4. **Pre-Phase 14 Security & Data Follow-Ups Completed**:
   - **Salt Verification**: `AuthService.HashPassword` generates a cryptographically random 16-byte salt per call using `RandomNumberGenerator.GetBytes(16)`. Reused hashes were artifact of legacy demo seeding.
   - **Client/Coordinator Login Audit**: `dbo.Users` entries (`client.shopee`, `coordinator.*`) cannot log into FOMS; FOMS requires an `Employees` record with one of the 4 finance roles. Client role is blocked with 403.
   - **Script Audit**: No `update_*.py` helper scripts exist in the repository; scratch scripts reside in ephemeral artifact scratchpads and contain no secrets.
   - **Frontend Add Employee Test Coverage**: Added `employeeFormPositionAndRole.test.tsx` (5 comprehensive integration tests covering Position required, derived role, no default, no CEO option, and temporary password modal). Frontend test count raised from 159 to 164.
   - **Migration Rollback Validation**: `AddPositionAndRoleNormalization` tested on isolated test DB `FOMS_MIGRATION_TEST_DB` through full Up -> Down -> Up cycles. Role restoration and column drops verified.
   - **Secrets to Rotate for Release**: Demo accounts (`EMP-001` through `EMP-004`, `client.shopee`, `coordinator.*`) and their historical hashes flagged for rotation upon release.

---

## Phase 14 Schema & Governance Approvals (Confirmed 2026-11-10)

1. **Test Counts Across All Four Components**:
   - `FOMS.Tests.dll`: 312 passed, 0 failed, 0 skipped
   - `foms-ai-gateway.Tests.dll`: 40 passed, 0 failed, 0 skipped
   - `Frontend (Vitest)`: 164 passed across 16 test suites, 0 failed
   - `AI-Service (pytest 9.2.0 on Python 3.11.9)`: 31 passed, 0 failed, 0 skipped
   - **Grand Total**: 547 passed, 0 failed, 0 skipped

2. **Setting (B) Configuration & Audit Schema (`dbo.CeoApprovalThresholdSettings`)**:
   - `SettingKey` is not unique; supports historical effective dating (`EffectiveFrom`, `EffectiveTo`).
   - Requests are evaluated against the threshold in force on the request creation date (`EffectiveFrom <= requestDate AND (EffectiveTo IS NULL OR EffectiveTo > requestDate)`).
   - `ThresholdAmount` defaults to `NULL` (no default seed; strictly neither 10,000 nor 50,000).
   - Fail-safe rule: when `ThresholdAmount` is `NULL`, transactions exceeding Limit (A) (`10,000.00`) mandate the CEO tick.
   - Authority: Only Finance Manager may write. Setting (B) is rejected if lower than Limit (A) unless explicitly overridden.
   - Dedicated change log: `dbo.CeoApprovalThresholdChangeLogs` captures `LogId`, `SettingId`, `OldAmount`, `NewAmount`, `ChangedBy`, `ChangedAt`, and mandatory `Reason`.

3. **In-Person Approval Records Schema (`dbo.CeoApprovalRecords`)**:
   - Record created only upon decision (`Approved` or `Rejected`). "Pending" is derived dynamically and never stored.
   - Complete immutability: Database trigger strictly blocks all `UPDATE` and `DELETE` operations.
   - Corrections: Recorded as new rows referencing `OriginalRecordId`, requiring `CorrectionReason` and confirmation by a second person (`CorrectionConfirmedBy`, `CorrectionConfirmedAt`). Gates evaluate the latest effective record.
   - Database CHECK constraints:
     - `ItemType` in 8 allowed items (`AnnualBudget`, `BudgetTransfer`, `BudgetCarryOver`, `BudgetOverride`, `Reimbursement`, `CashOutflow`, `InvoiceDiscount`, `AnnualFinancialStatement`).
     - `RejectionReason` mandatory when `Status == 'Rejected'`.
     - `HasAttachment = 1` permitted only on items 1, 6, and 8.
     - `ApprovalDate` cannot be in the future (evaluated in `Asia/Manila` / UTC+8).
   - Single active original record per `ItemType` + `ItemId` enforced by unique filtered index.
   - `EnteredBy` references `dbo.Employees(Id)` and must be the Finance Manager.
   - Items 2 & 3 (`BudgetTransfer`, `BudgetCarryOver`) mandate Head Accountant confirmation (`ConfirmedBy`, `ConfirmedAt`), where confirmer differs from requester and `EnteredBy`.

4. **Attachment Security & Retention**:
   - Magic bytes validation for PDF, JPG, PNG.
   - 10 MB strict file size limit.
   - Stored outside public web root (`Storage/SecureAttachments/CeoApprovals/`).
   - File names sanitized with GUIDs; 5-year retention flag.
   - Download restricted to Finance Manager and Head Accountant.

5. **Legacy Approvals & Deprecations**:
   - Database audit: Exactly 0 records in `FOMS_DB` are approved or released by `EMP-CEO` or containing `CEO`.
   - Legacy `IsCeoOrPresident` checks removed from all 8 business gates; old approval roles marked deprecated.

---

## Phase 14 Architecture & Immutability Refinements (Confirmed 2026-11-10)

1. **Confirmation Model via Dedicated Insert-Only Table (`dbo.CeoApprovalConfirmations`)**:
   - Because `dbo.CeoApprovalRecords` is strictly immutable (trigger prohibits `UPDATE`), post-insert confirmation fields (`ConfirmedBy`, `ConfirmedAt`, `CorrectionConfirmedBy`, `CorrectionConfirmedAt`) were dropped from `dbo.CeoApprovalRecords` via migration `20261117180000_RefineCeoApprovalConfirmationsAndImmutability`.
   - Dedicated child table `dbo.CeoApprovalConfirmations` introduced (`ConfirmationId`, `RecordId`, `Kind` ['Recording' or 'Correction'], `ConfirmedBy`, `ConfirmedAt`).
   - Strict immutability trigger `TR_CeoApprovalConfirmations_Immutable` applied to block `UPDATE` and `DELETE`.
   - Server-enforced rules:
     - Items 2 and 3 (`BudgetTransfer`, `BudgetCarryOver`) become effective only with a `Recording` confirmation by the Head Accountant, who must differ from the requester and from `EnteredBy`.
     - A correction record becomes effective only with a `Correction` confirmation from a second person who differs from `EnteredBy`.
     - Business gates evaluate the latest effective record.

2. **Threshold Settings & Change Log Immutability**:
   - `dbo.CeoApprovalThresholdChangeLogs`: `SettingId` set to `NOT NULL` with foreign key to `dbo.CeoApprovalThresholdSettings(SettingId)`. Immutability trigger `TR_CeoApprovalThresholdChangeLogs_Immutable` applied to block `UPDATE` and `DELETE`.
   - `dbo.CeoApprovalThresholdSettings`: Added database CHECK constraint `CHECK (ThresholdAmount IS NULL OR ThresholdAmount > 0)` and unique filtered index `IX_CeoApprovalThresholdSettings_OneActiveKey` on `(SettingKey)` where `IsActive = 1`. Service layer enforces strictly non-overlapping effective date ranges.

3. **Item Existence & Validation Governance**:
   - Because `ItemId` is polymorphic without a foreign key, the gate service verifies that the referenced item exists and matches `ItemType` before any approval/rejection record can be created.
   - Lifecycle rule: After a rejection, the same item changes only via a confirmed correction record. Any brand-new request is assigned a new `ItemId`.

4. **EF Core Model Snapshot Synchronization**:
   - `ApplicationDbContextModelSnapshot.cs` fully synchronized with all database entities and hand-crafted migrations. Verified: `dotnet ef migrations add` produces a completely empty migration (`EmptyCheck.cs`).

5. **E2E Testing & Test Cleanliness**:
   - Because database triggers prohibit `DELETE` on `CeoApprovalRecords`, `CeoApprovalConfirmations`, and `CeoApprovalThresholdChangeLogs`, E2E test suites must use disposable, isolated test databases rather than table cleanup/deletes.

6. **Gate Categorization**:
   - **Always-on Tick Items**: (1) Annual Budget, (2) Budget Transfer, (3) Budget Carry-over, (7) Invoice Discount, (8) Annual Financial Statements.
   - **Threshold-Dependent Items**: (4) Budget Override, (5) Reimbursement, (6) Cash Outflow (governed by Setting B with fail-safe to Limit A when unset).


---

## Phase 15 Approvals & Directives (Confirmed 2026-11-10)

1. **Department Codes & Governance**:
   - Canonical 4+1 departments:
     - `DEP-FIN` — Finance & Accounting
     - `DEP-CCS` — Coordinator & Customer Service Team
     - `DEP-DSP` — Dispatch Team
     - `DEP-FWD` — Forwarding Team
     - `DEP-OVH` — Company-wide (Overhead) (5th budget line, FM-only selectable, enforced server-side).
   - Server-side validation normalizes input and strictly rejects unknown department codes.
   - Legacy `DEP-OPS` (Operations) is strictly legacy read-only: not selectable for new requests, preserved for historical data and legacy tests.
   - Rows currently using `DEP-OPS`: 4 rows in `dbo.CashAdvances` (`CA-001`, `CA-002`, `CA-003`, `CA-004`). No remapping without explicit user instruction.
   - Overhead budget line (`DEP-OVH`): exists per category per month, allocated exclusively by the Finance Manager (`FIN-01`). Zero seeded default amounts.

2. **Last-Used Department Suggestion Chip (FIN-26, Option A)**:
   - Every cash advance, direct disbursement, and reimbursement request requires a department chosen by the requester.
   - Form loads with empty department (`placeholder="Select Department"`). No default is ever selected by client or server.
   - Suggestion chip appears only if the current user has previously submitted a request, showing their own last-used department.
   - `DEP-OVH` (Company-wide Overhead) is never suggested to non-FM users.
   - Server rejects any request submitted without an explicit valid department code.

3. **Department Change Audit & Immutability (`dbo.DepartmentChangeLogs`, R5.11 Item 5)**:
   - Dedicated table `dbo.DepartmentChangeLogs` (`LogId`, `RequestType`, `RequestId`, `OldDepartmentCode`, `NewDepartmentCode`, `Reason`, `ChangedBy`, `ChangedAt`).
   - Protected by database trigger `TR_DepartmentChangeLogs_Immutable` (insert-only).
   - Only the Finance Manager can change a department prior to approval.
   - Changing a department requires a non-empty reason and automatically triggers a budget re-check.
   - Requests are permanently locked upon approval (`IsDepartmentLocked = true`). Post-approval fixes must use logged correction entries.

4. **CSH-09 Setting (B) Status**:
   - Limit (B) remains unconfigured (`NULL`) pending Finance confirmation. Fail-safe to Limit (A) (`₱10,000.00`) remains active.

---

## Phase 16 Approvals & Directives (Confirmed 2026-11-10)

1. **Acceptance of Phase 14 & 15**:
   - Phase 14 and Phase 15 accepted with verification checks confirmed.
   - Fail-safe for CSH-09 Setting (B) to Limit (A) (₱10,000.00) remains active until Finance confirmation.

2. **Phase 16 Test Migration & Transition Strategy**:
   - List ALL tests and code using old statuses across backend, frontend, E2E, and AI gateway.
   - Replace old tests in a separate dedicated commit. Do not delete them; retain a test for legacy status mapping.
   - Test 4 assertion: voiding an Approved invoice posts NO journal entry (AR posts strictly at Sent). Void allowed only by Head Accountant prior to Sent. After Sent, adjustments require a credit memo.
   - Additive, reversible migration for existing dbo.Invoices rows (Pending HA Verification -> For Review, Returned to Creator -> Draft). Show mapping before applying to local dev. Test Down on a copy.
   - AR and accrual revenue post once at Sent with an idempotency key (invoice_id + event). Payments accepted only for Sent or Received invoices.
   - Edge case: Head Accountant creating an invoice cannot verify their own invoice (no-self-approval). Awaiting user decision on who verifies.

3. **Invoice Verification Edge Case (Confirmed 2026-11-10)**:
   - Option 1 Approved: If the Head Accountant created/encoded the invoice, the Finance Manager verifies and approves the invoice (For Review -> Approved) to enforce no-self-approval without deadlock.

---

## Phase 17 Approvals & Directives (Confirmed 2026-11-10)

1. **Refund Policy & System Hygiene**:
   - Keep the refund block in `CashOutflowsController.cs` active (rejects `Refund` category with HTTP 400).
   - Keep `dbo.RefundRequests` strictly read-only and unmapped (historical retention only, `IsLegacyHistory = 1`).
   - Rename UI label in `LiquidationValidation.tsx` and settlements from "Leftover Refund to Cashier" to "Leftover Cash Return to Cashier" to clarify internal cash inflow vs. client outflow.

2. **Payment Validation & Delegate Configuration (PAY-03, Row 4)**:
   - Build the delegate feature but keep it disabled by default.
   - Only the Finance Manager validates payments until a delegate is explicitly configured and logged in settings.
   - Enforce maker-checker rule: `validator != recorder` (the validator cannot validate payments they recorded).
   - Strict privilege boundary: A payment delegate cannot record CEO/President approval records.

3. **Client Credit Application on Already-Paid Invoices (Item 7 / R4.11 Item 3)**:
   - Implement Option A (automatic) as a configurable setting, marked pending Finance confirmation (R4.11 item 3 is an open question for Finance, not a recommendation).
   - Show the applied credit as its own separate line **after the grand total**, not deducted from the subtotal.
   - Cap applied credit at the invoice total (BIL-05) so the invoice balance does not turn negative.
   - Carry any remainder forward as an unapplied client credit balance.
   - Log the VAT treatment of applied credit as an open question for Finance.

4. **AR Aging Buckets & Reminders (ARC-03, ARC-04, ARC-05)**:
   - Canonical aging buckets: `Not Yet Due`, `Due Soon (configurable)`, `1-30`, `31-60`, `61-90`, `90+`.
   - Buckets and overdue flags are dynamically derived from `DueDate` and never stored in the database.
   - `Due Soon` default threshold is **7 days** before due date, configurable in settings.
   - Follow-up reminders: 7 days after billing if unacknowledged, then due soon, then overdue.

5. **Phase 17 Post-Review Corrections & Clarifications (Confirmed 2026-11-10)**:
   - **IAM-06**: Clients have no login, no credentials, and no role in FOMS. Replaced test client token with assertions verifying `FomsRoles.AllCanonicalRoles` excludes "Client", `AuthService.LoginAsync` rejects client logins with `UnauthorizedAccessException`, and invoice querying and detail viewing strictly isolate client amounts.
   - **ARC-04**: The aging report export endpoint `/api/invoices/aging-report/export` produces genuine `.xlsx` files (`application/vnd.openxmlformats-officedocument.spreadsheetml.sheet`) using ClosedXML, with formatted headers and summary metrics, while preserving optional `?format=csv`.
   - **Frontend Test File Count Reconciliation**: Confirmed exactly **16 test files** (164 tests). The Phase 11 baseline was 15 files (159 tests); Phase 13 added `employeeFormPositionAndRole.test.tsx` (+5 tests). The number "18" refers to individual test counts within two test suites (`aiIntelligenceLayer.test.tsx` has 18 tests and `canonicalInvoiceQueuesAndBadges.test.tsx` has 18 tests), not the count of test files. No test files were deleted.
   - **R4.5 Credit Memo Accounting on Paid Invoices**: Approving a credit memo on an already Paid invoice posts `Dr Cargo Claims (Sales Allowances) [4200] / Cr Unapplied Client Credits [2160]` via `PostCreditMemoJournalEntryAsync`. Verified zero cash outflow and client `AvailableCredit` balance update.

---

## Phase 18 Approvals & Directives (Confirmed 2026-11-10)

1. **Routing & Authority**:
   - **Liquidation Validation**: Assistant of Finance Manager validates liquidations alone at or below the CSH-09 threshold. Above the threshold, the Finance Manager also signs off, and the CEO/President approval record applies where Phase 14 requires it.
   - **Reimbursement Workflow**: Validated by Assistant of Finance Manager, always approved by the Finance Manager (`Committed`), then marked `Paid` (`Actual`).
   - **CEO/President Tick (`R5.3` items 4, 5, 6)**: Applies only above the CSH-09 threshold, and only the Finance Manager can record it.
   - **Leftover Cash Returned**: Classified strictly as a cash inflow, never a negative disbursement.
   - **Late Liquidation**: "Late" is a flag (`isLate`), never a status. Statuses follow canonical lifecycle.
   - **Direct Disbursement**: Recognized as `Actual` at release.
   - **CSH-09 Setting**: Must be a configurable database setting (`dbo.CeoApprovalThresholdSettings`), failing safe to Limit (A) (`₱10,000.00`) while unset.

2. **Implementation & Verification Status**:
   - **Cash Advance & CEO Gate**: Release enforces CEO/President approval record (`CeoApprovalItemTypes.CashOutflow`) when amount exceeds CSH-09 threshold setting. Requesters cannot approve own advance.
   - **Liquidation**: Validated by Assistant of Finance Manager (or FM as explicit decision; validator ≠ submitter strictly enforced). Leftover cash return records an independent `CashInflow`. Late status is a dynamic boolean flag `isLate`. Fixed dropdown reasons supported (`wrong receipt date or amount`, `manual fuel receipt`, `late submission`, `other with remarks`). Returns status `"For Correction"` without posting GL entries.
   - **Reimbursement**: Validated by Assistant FM, approved by FM (`Committed`), paid by cashier/FM (`Actual`). Enforces CEO gate above threshold.
   - **Direct Disbursement (CSH-12)**: Moves directly from `Committed` to `Actual` at release with zero liquidation required.
   - **Net Cash Period Chaining**: `GET /api/CashFlow/net-cash` computes beginning cash for later periods as previous period's ending cash. Validated client payments only are counted in inflows. Account 1030 is mapped to 1010.
   - **Commitment Release Verification**: Verified via unit tests that Cancelled and Rejected cash advances release their committed budget reservations, and unused commitment is released back to Available upon liquidation validation.
   - **Frontend Rejection Dropdown Test**: Added to `emp001Flow.test.tsx` covering canonical dropdown selection, required remarks for 'other with remarks', and update payload.
   - **All 4 Test Suites Green**: Backend (401 passed), Gateway (40 passed), Frontend (16 test files, 165 passed), AI-service (31 passed). Full details in `phase-18-report.md`.

3. **Phase 18 Follow-ups & Open Questions Logged for Finance**:
   - **CSH-09 Threshold Value**: The ₱10,000 setting remains a provisional placeholder pending Finance confirmation (`R5.11 q2`).
   - **Account 1030 (Petty Cash)**: Mapped into Account 1010 (Cash on Hand). Open Question: Confirm whether Petty Cash (1030) should be formally added to Chart of Accounts (R4.6) or permanently consolidated under 1010.
   - **Reimbursement CEO Tick Count**: Open Question: Does a reimbursement above the threshold need one tick (`Item 5: Reimbursement`) or two (`Item 5: Reimbursement` and `Item 6: CashOutflow`)?
   - **General Ledger Postings (R4.5)**: Phase 18 events currently record cash flows and update budget lines, but do not yet post double-entry general ledger journal entries (`dbo.GeneralLedgerEntries`). These journal entries will be integrated into the Centralized Double-Entry Engine in Phase 19.

---

## Phase 19 Governance, Chart of Accounts (R4.6), and Posting Architecture (2026-11-10)

### 1. Test Suite Audit & Frontend Baseline
- **Audit Findings**: The 18-file list referenced in step 3248 was a hallucinated model output block generated while task-3244 was running asynchronously. Task-3244's actual execution log on disk (`.system_generated/tasks/task-3244.log` and step 3252 system notification) completed with **16 test files (164 tests)**.
- **Current Baseline**: 16 test files, 165 tests (with the rejection dropdown test added in `emp001Flow.test.tsx`), all passing cleanly.
- **Full Verification**: Git history, reflog, stash, and full recursive disk search confirm zero deleted or missing test files.

### 2. Standardized Chart of Accounts (R4.6 Alignment)
The general ledger strictly adopts the R4.6 account codes without collision or reuse:

| Account Code | Account Title | Category / Flow | Source / Status |
|---|---|---|---|
| **1010** | Cash on Hand | Asset | Addition (Petty Cash / Physical Cash Collections & Inflows; R4.11 q2) |
| **1020** | Cash in Bank | Asset | Addition (Operating Bank Account Deposits / Releases; R4.11 q2) |
| **1030** | Petty Cash | Asset | Addition (Mapped to 1010 pending Finance confirmation; R4.11 q2) |
| **1100** | Accounts Receivable (Trade) | Asset | R4.6 Standard |
| **1150** | Advances to Officers & Employees | Asset | R4.6 Standard |
| **2010** | Reimbursements Payable | Liability | R4.6 Standard |
| **2100** | Output VAT Payable | Liability | R4.6 Standard |
| **2110** | Unapplied Client Credits | Liability | R4.6 Standard |
| **2120** | Unidentified Payments Held | Liability | R4.6 Standard |
| **2130** | Accounts Payable (Trade) | Liability | Addition (Non-employee trade payables; R4.11 q2) |
| **2200** | Accrued Expenses | Liability | R4.6 Standard |
| **3010** | Common Stock | Equity | R4.6 Standard |
| **3020** | Additional Paid-in Capital | Equity | R4.6 Standard |
| **3030** | Retained Earnings | Equity | R4.6 Standard |
| **4010** | Freight Revenue | Revenue | R4.6 Standard |
| **4020** | Valuation Charge Revenue | Revenue | R4.6 Standard |
| **4030** | ODA (Out-of-Delivery Area) Revenue | Revenue | R4.6 Standard |
| **4040** | Fuel Surcharge Revenue | Revenue | R4.6 Standard |
| **4900** | Sales Discounts | Contra-Revenue | R4.6 Standard (Approved discounts) |
| **4910** | Cargo Claims & Sales Allowances | Contra-Revenue | R4.6 Standard (Credit memos / claims) |
| **5010** | Fuel | Expense | R4.6 Standard |
| **5020** | Toll | Expense | R4.6 Standard |
| **5030** | Parking | Expense | R4.6 Standard |
| **5040** | Food Allowance | Expense | R4.6 Standard |
| **5050** | Vulcanizing | Expense | R4.6 Standard |
| **5060** | Repairs & Maintenance | Expense | R4.6 Standard |
| **5070** | Hotel & Lodging | Expense | R4.6 Standard |
| **5080** | Office Supplies | Expense | R4.6 Standard |
| **5090** | Miscellaneous Operating Expense | Expense | R4.6 Standard |

### 3. Double-Entry Posting Rules (R4.5 Strict Alignment)
- **Central Double-Entry Engine**: Every posted entry balances ($\sum \text{Debits} == \sum \text{Credits}$). Unbalanced entries are rejected.
- **Line Metadata**: Every entry line carries `AccountCode`, `DepartmentCode`, and `Category`.
- **Database Immutability**: Enforced via SQL Server triggers on `dbo.JournalEntries` and `dbo.GeneralLedgerEntries` (no `UPDATE` or `DELETE`).
- **Period Locking**: Posting fails with validation error if target date falls within a locked accounting period (`AccountingPeriods.IsLocked = 1`).
- **Commitments**: Commitments remain strictly out of the general ledger and are tracked purely in budget control tables.
- **Posting Events Summary**:
  1. **Invoice Sent**: `Dr Accounts Receivable [1100]` / `Cr Freight Revenue [4010]`, `Cr Valuation Charge Revenue [4020]`, `Cr ODA Revenue [4030]`, `Cr Fuel Surcharge Revenue [4040]`, `Cr Output VAT Payable [2100]`.
  2. **Invoice Void**: Reverses Sent entry only if previously posted.
  3. **Invoice Approved Discount**: `Dr Sales Discounts [4900]` / `Cr Accounts Receivable [1100]`.
  4. **Validated Payment**: `Dr Cash in Bank [1020]` / `Cr Accounts Receivable [1100]`.
  5. **Overpayment**: `Dr Cash in Bank [1020]` / `Cr Unapplied Client Credits [2110]` held for Finance resolution (invoice NEVER directly marked Paid per `PAY-10`).
  6. **Unidentified Payment**: `Dr Cash in Bank [1020]` / `Cr Unidentified Payments Held [2120]`.
  7. **Credit Memo (Paid Invoice)**: `Dr Cargo Claims & Sales Allowances [4910]` / `Cr Unapplied Client Credits [2110]`.
  8. **Cash Advance Release**: `Dr Advances to Officers & Employees [1150]` / `Cr Cash in Bank [1020]` (or Cash on Hand [1010]).
  9. **Liquidation Validated**: `Dr Expense [5010-5090] by Category` / `Cr Advances to Officers & Employees [1150]`.
  10. **Leftover Cash Returned**: `Dr Cash on Hand [1010]` / `Cr Advances to Officers & Employees [1150]`.
  11. **Reimbursement Approved**: `Dr Expense [5010-5090] by Category` / `Cr Reimbursements Payable [2010]`.
  12. **Reimbursement Paid**: `Dr Reimbursements Payable [2010]` / `Cr Cash in Bank [1020]` (or Cash on Hand [1010]).
  13. **Direct Disbursement Released**: `Dr Expense [5010-5090] by Category` / `Cr Cash in Bank [1020]` (or Cash on Hand [1010]).

### 4. Tie-Out Verifications
1. **AR to Aging (`ARC-07`)**: Account `1100` ledger balance == Total Outstanding in AR Aging Report.
2. **Cash to Net Cash & Bank Rec**: Accounts `1010 + 1020` == Ending Cash in Net Cash Flow and Bank Reconciliation unadjusted book balance.
3. **Advances to Unliquidated List**: Account `1150` == Total outstanding unliquidated cash advances.

---

## Phase 20 Revision 5 Addendum Governance: Approvals, Roles, and Personnel Architecture (2026-11-10)

### 1. In-Person CEO/President Approval Governance (`IAM-07` to `IAM-10`)
- **Zero CEO/President Login or Role**: No login, user credentials, employee record, or authorization claim for CEO or President exists anywhere in FOMS.
- **Physical In-Person Approval Table**: `dbo.CeoApprovalRecords` is the single source of truth for documented approvals (`ItemType`, `ItemId`, `Status`, `ApprovalDate`, `Notes`, `HasAttachment`, `EnteredBy`, `EnteredAt`, `RejectionReason`).
- **Database Immutability**: Protected by trigger `TR_CeoApprovalRecords_Immutable` blocking `UPDATE` and `DELETE`. Corrections are appended as linked correction records.
- **Finance Manager Exclusive Recording**: Only the Finance Manager (`FomsRoles.FinanceManager`) can record an approval or rejection. Attempts by any other role throw HTTP 403 / `UnauthorizedAccessException`.
- **The 8 Gated Transaction Items**:
  1. `AnnualBudget`: Allocation gated on board approval (optional attachment permitted).
  2. `BudgetOverride`: Over-budget spend override (attachments strictly prohibited).
  3. `BudgetTransfer`: Budget transfers and carry-overs require in-person approval record plus Head Accountant maker-checker recording confirmation (attachments strictly prohibited).
  4. `CashOutflow` (Cash Advance above CSH-09 threshold): Evaluated against threshold in force as of request date; attachments strictly prohibited.
  5. `Reimbursement` (Above CSH-09 threshold): Evaluated against threshold in force as of request date; attachments strictly prohibited.
  6. `CashOutflow` (Direct Disbursement above CSH-09 threshold): Evaluated against threshold in force as of request date; board resolution/minutes attachment permitted.
  7. `BadDebtWriteOff` / `InvoiceDiscount`: Gated on CEO/President in-person approval record (attachments strictly prohibited).
  8. `AnnualFinancialStatement`: Gated on CEO/President approval record before final issuance (audited statement attachment permitted).
- **Threshold Setting Governance (CSH-09)**:
  - Setting B (`CSH-09_CEO_THRESHOLD`) is tested against the threshold in force as of the request date.
  - An unconfigured or deactivated threshold fails safe to Limit A (`ApprovalThresholdConfig.DefaultAssistantFmThreshold`, ₱10,000) requiring mandatory approval record.
  - Threshold amount cannot be set lower than Limit A.
- **Rejection Reservation Release**: Recording a rejection on a Cash Advance or Budget Override releases the Committed budget reservation and records the mandatory rejection reason.

### 2. Department Governance (`FIN-26`)
- `DEP-OVH` (Company-wide Overhead) is strictly selectable only by the Finance Manager, enforced both server-side in `FomsDepartments.Normalize` and client-side by filtering it out of dropdowns for non-FM users.
- Database default constraints `('DEP-OPS')` dropped across `CashAdvances`, `DirectDisbursements`, and `Reimbursements` with C# initializers set to `string.Empty` to ensure all expense submissions require an explicit department selection with zero defaulting. Legacy rows remain untouched.

### 3. Personnel Architecture & DMS Operational Alignment (`IAM-11`, `R5.6`)
- **Add Employee Form Layout**:
  - `Position` (required, starts empty, grouped into Finance and Non-Finance positions).
  - `FOMS Role` (read-only, derived automatically from Position).
  - `DMS Operational Role` (read-only `None` for finance positions; active dropdown with options `Admin`, `Encoder`, `Driver`, `None (no DMS access)` for non-finance positions).
  - Responsive mobile stacking (one field per row on mobile viewports).
- **Zero Department on Personnel**: Personnel records carry strictly NO department field (Check 2: department belongs to expense transactions, not employee records).
- **Client Role Removal**: Client role is permanently forbidden from employee registration; client accounts have zero login access to FOMS (`IAM-06`).
- **Database Seed Normalization**:
  - `ROLE-004` Description renamed to `"Assistant of Finance Manager"`.
  - `dbo.Users` row 1 (`user_id = 1`) renamed to `"Finance Manager Test User"`.
  - `dbo.Users` row 8 (`CLIENT-001`) deactivated (`is_active = 0`).
  - `dbo.Users` row 5 (`EMP-005`, "Billing Staff", `role_id = 1`) kept untouched and flagged for user review.

---

## Phase 21 Revision 5 Verification & Gap Closure Decisions (2026-11-11)

### 1. Product Owner Decisions
- **PO Decision A (`EMP-005`, `user_id = 5`)**:
  - Remapped from `role_id = 1` (misassigned Finance Manager) to `role_id = 3` (`Accountant`).
  - Applied via database script [`scripts/Phase21_Remap_Emp005_To_Accountant.sql`](file:///c:/Users/Joana/Downloads/another%20one/CAPSTONE2/scripts/Phase21_Remap_Emp005_To_Accountant.sql) to `FOMS_DB`.
  - Verified only one user account (`EMP-001`) holds `role_id = 1`.
- **PO Decision B (`ROLE-005` & `ROLE-006`)**:
  - Kept in `dbo.Roles` for referential integrity and historical audit; login and role assignment are strictly blocked (`IAM-01`).
  - Only the 4 approved roles (`Finance Manager`, `Head Accountant`, `Accountant`, `Assistant of Finance Manager`) can authenticate or be assigned.
  - Omitted from Add Employee form dropdowns; blocked at server-level in `AuthService` (direct and SSO) and `UserService`.

### 2. Item 6 Cash Outflow Attachment Coverage (`G1`, `R5.3`)
- Cash Outflow above CSH-09 threshold (Item 6) covers Cash Advance release, Direct Disbursement release, and Reimbursement payment.
- Attachments are optional on all three outflow types under Item 6.
- The 8 items attachment matrix:
  - Items 1, 6, 8 allow attachments (8 mandatory, 1 & 6 optional).
  - Items 2, 3, 4, 5, 7 strictly forbid attachments (tick-only).
- Attachment flag renders `"Attached"`, `"No attachment"`, or `"N/A"` across DTOs and Excel exports.

### 3. Department Requirements on Requests (`G4`, `FIN-26`)
- All new expense requests require an explicit department with zero defaulting; legacy `DEP-OPS` is non-selectable and throws `ArgumentException`.
- DMS inbound cash advances missing department are automatically routed to Dispatch Team (`DEP-DSP`), subject to pre-approval department review and audit logging by the Finance Manager.

### 4. Governance Confirmations (`G5`, `R5.11`)
- Head Accountant recording confirmation is maker-checker verification only, blocking transfers/carry-overs from proceeding until confirmed. Head Accountant has zero budget-approval permissions.
- CSH-09 threshold is FM-only, audited in `dbo.CeoApprovalThresholdChangeLogs`, evaluated as of request date, and fails safe to Limit A (₱10,000) when unset.

### 5. Mobile Layout & External DMS Handoff (`G6`, `G7`)
- Add Employee modal supports 390px mobile viewports with responsive padding and flexible grid columns (`add-employee-390px.png`).
- Check 9 remains `UNVERIFIED (handoff doc only)`. Documented removal of pre-selected Encoder, addition of `None (no DMS access)`, and open question R5.11 Item 7 in `docs/prompt-set-2/dms-handoff.md`.

---

## Phase 22 Revision 5 Corrections: Authority, Gates, and Architecture Alignment (2026-11-11)

### 1. Product Owner Decisions
- **PO Decision C (DMS Requests Without Department)**:
  - Any inbound DMS cash advance request arriving without a department code is accepted into a `"Needs department"` status (`DepartmentCode = string.Empty`).
  - No cash outflow or automatic budget reservation is created.
  - The request cannot be budget-checked or approved until a department is set, either by the requester or by the Finance Manager with a logged reason (`FIN-26`).
  - Zero default department: the previous `DEP-DSP` fallback has been completely removed from `CashAdvancesController` and `DmsIntegrationService`.
- **PO Decision E (Unset CSH-09 Threshold Fail-Safe)**:
  - When the CSH-09 threshold is unset (`ThresholdAmount = null`), **every cash outflow requires CEO approval** (`EffectiveCeoLimit = 0m`).
  - Zero amount is assumed; the previous fallback to Limit A (₱10,000) has been eliminated.
  - Configurable database setting `CSH-09_CEO_THRESHOLD` in `dbo.CeoApprovalThresholdSettings` is managed exclusively by the Finance Manager with audit logs in `dbo.CeoApprovalThresholdChangeLogs`.

### 2. Items Labeled ASSUMED (`R5.11`)
- **Limit A (₱10,000)**:
  - *Origin*: Phase 7 commit `8eed0bf` (`ApprovalThresholdConfig.cs`) as Assistant FM threshold.
  - *Status*: Labeled `ASSUMED` in code, `DECISIONS.md`, and configuration. Not defined in Rev 5 addendum. Eliminated as a fallback for CSH-09 per Decision E.
- **Non-Finance Operational Positions**:
  - *List*: "Courier, Dispatcher, Driver, Hub Supervisor, Sorter".
  - *Origin*: Phase 20 commit `bb4d0ee` in `ProfilePage.tsx`. Operational assumptions, not sourced from DMS schema.
  - *Status*: Labeled `ASSUMED` in `FOMS.Domain.Constants.EmployeePositions`, `DECISIONS.md`, and `dms-handoff.md`.
  - *Architecture*: Unified to single server-side source of truth (`GET /api/users/positions`). Frontend fetches from this endpoint on mount with `ASSUMED` fallback constants.

### 3. Canonical R5.3 8-Item CEO Gate Mapping
- BadDebtWriteOff is not an item in `R5.3` and has been completely removed from the gate service and DTOs.
- Corrected 8-Item Mapping:
  1. `AnnualBudget`: Blocks allocation. Attachment: **Optional**.
  2. `BudgetTransfer`: Blocks transfer. Attachment: **Prohibited**. Requires Head Accountant maker-checker recording confirmation.
  3. `BudgetCarryOver`: Blocks carry-over. Attachment: **Prohibited**. Requires Head Accountant maker-checker recording confirmation.
  4. `BudgetOverride`: Blocks override approval & Committed reservation. Attachment: **Prohibited**.
  5. `Reimbursement`: Blocks reimbursement approval & Committed reservation above CSH-09 threshold. Attachment: **Prohibited**.
  6. `CashOutflow`: Blocks release/payment above CSH-09 threshold (covers cash advance release, direct disbursement release, and reimbursement payment). Attachment: **Optional**.
  7. `InvoiceDiscount`: Blocks discount deduction from invoice total (BIL-07). Attachment: **Prohibited**.
  8. `AnnualFinancialStatement`: Blocks sign-off/closing. Attachment: **Optional** (reverted from Mandatory per user directive; logged under open question R5.11 q3).

### 4. Repeatable Migration for EMP-005
- Reversible EF Core migration `20261117220000_RemapEmp005ToAccountant.cs` created and applied to `FOMS_DB`.
- Verified against fresh database using `FromScratchMigrationCheckTests` (clean 32-migration sequence).
- User `EMP-005` permanently assigned `role_id = 3` (`Accountant`). Finance Manager login is held exclusively by `EMP-001` (`crystalyn`).

### 5. Excel Export Enhancements
- `ExportRecordsToExcelAsync` in `CeoApprovalGateService` and `ExportDepartmentChangeLogsToExcelAsync` in `DepartmentService` generate full Excel `.xlsx` workbooks via `ClosedXML.Excel`.
- Columns assert "No attachment" flag, rejection reasons, approver labels, and old/new department codes and names.

### 6. Adjusting Entries Governance (`FIN-23`)
- **Authority**: Adjusting entries are created **exclusively by the Finance Manager** (`ROLE-001`, `FomsRoles.IsFinanceManager`). Any attempt by another role (Accountant, Head Accountant, Assistant of Finance Manager, Staff) is rejected with 403 Forbidden.
- **Role Alignment**: There is no Bookkeeper role in FOMS; authority conforms strictly to the four canonical roles in `IAM-01`.
- **Reason Mandatory**: A non-empty reason is strictly required upon creation.
- **Posting Lifecycle**: Created in `Pending` status. Financial impact (balance reduction, credit memo amount, GL posting) is blocked until the adjustment is approved before posting.
- **Status Lifecycle Deviation**: Spec R4.4 specifies adjusting entry statuses as `Draft`, `Approved`, `Posted`, `Rejected`. Current FOMS implementation uses `Pending`, `Approved`, `Rejected` (where `Pending` represents unapproved draft/review state, and `Approved` immediately posts journal entries to General Ledger and applies invoice balance deductions). Logged as an active deviation.
- **Approver Role**: PROPOSED (R5.11 item 1 applied to R4.11 q4), pending Finance confirmation: Head Accountant approves adjusting entries created by the Finance Manager, and creator self-approval is strictly forbidden (Separation of Duties).
- **Closed Period Lock**: Creation and approval of adjusting entries are strictly blocked if transaction date falls within a closed accounting period (`FIN-23 / R4.2`).
- **Open Question `R4.11 q4`**: Who approves adjusting entries when the Finance Manager is the creator? PROPOSED: Head Accountant (per R5.11 item 1), pending Finance confirmation.

### 7. Unset CSH-09 Threshold Fail-Safe & Go-Live Setup (`PO Decision E`)
- **Go-Live Setup Step**: During initial deployment / go-live configuration, the Finance Manager must navigate to System Settings and set the official `CSH-09_CEO_THRESHOLD` amount in `dbo.CeoApprovalThresholdSettings`.
- **Clear Actionable Message**: If a cash outflow is initiated while the threshold is unset, the gate fails safely and outputs an explicit, actionable message to prevent silent blockage:
  `"Gate blocked: Setting (B) (CSH-09 CEO threshold) is not configured in Go-Live setup. Under PO Decision E, every cash outflow requires an in-person CEO/President approval record until a threshold is configured in System Settings by the Finance Manager. Transaction strictly requires CEO/President approval record for {itemType} '{itemId}'."`

### 8. Department Rules Traced to `IAM-11` and `FIN-26` (with `R5.11 Item 5`)
- **IAM-11 (Employee Records Department Independence)**:
  - Employee records (`dbo.Employees`) and user profile forms have **zero department field**.
  - Roles never depend on department. Personnel are structured strictly by `Position`, canonical `FOMS Role` (IAM-01), and optional `DMS Operational Role`.
  - Add Employee form in `ProfilePage.tsx` strictly presents 3 fields: Position, FOMS Role, DMS Role.
- **FIN-26 & R5.11 Item 5 (Mandatory Department & Immutability Governance on Expense Requests)**:
  - Department code is strictly required on all financial expense transactions: budget lines, cash advances, direct disbursements, and reimbursements.
  - Zero default department: legacy `'DEP-OPS'` defaults dropped from database schemas and initialized to `string.Empty` in domain models.
  - Requesters have no default department; inbound DMS requests without a department enter `"Needs department"` status (`PO Decision C`).
  - Pre-approval department changes are restricted exclusively to the Finance Manager and strictly require a non-empty reason logged to `dbo.DepartmentChangeLogs` (`FIN-26`).
  - Department changes automatically re-run the budget availability check against the newly assigned department's available balance.
  - Once approved, requests are permanently locked; any subsequent changes must be recorded as logged correction entries.

### 9. Open Questions Log
- **`R5.11 q3`**: Is an attachment for Item 8 (`AnnualFinancialStatement`) strictly optional or mandatory? Reverted to Optional per PO directive; "No attachment" is recorded and exported when omitted.
- **`R4.11 q4`**: When the Finance Manager creates an adjusting entry (`FIN-23`), who holds the authority to approve it before posting? (Pending PO confirmation).








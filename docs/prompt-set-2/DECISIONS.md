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

5. **R5.11 Items 1 to 7**:
   - **Item 1 (Maker-Checker)**: Head Accountant confirms the recording of transfers and carry-overs requested by the Finance Manager. (Confirmation is not budget approval).
   - **Item 2 (CSH-09 Setting)**: CSH-09 is a setting changeable only by FM, logged with date, evaluated against the threshold in force on the request date.
   - **Item 3 (Attachment Optional)**: Attachments are optional on items 1, 6, and 8; show "No attachment" flag in record and Excel export.
   - **Item 4 (Non-Finance Positions)**: Copied from what DMS already stores; do not invent positions.
   - **Item 5 (Department Changes & Locks)**: FM may change department before approval with a logged reason (re-checking budget). Locked after approval; fixes via logged correction entries.
   - **Item 6 (Company-wide Budget Line)**: 5th budget line "Company-wide (Overhead)" selectable by the Finance Manager only.
   - **Item 7 (DMS Client Option)**: Open / deferred to external DMS.

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


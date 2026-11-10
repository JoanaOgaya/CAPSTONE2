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

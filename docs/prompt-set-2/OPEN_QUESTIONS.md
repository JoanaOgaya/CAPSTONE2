# Prompt Set 2: Open Questions Waiting for User Decision

Status updated: 2026-11-10 (Phase 15 In Progress)

---

## Active Questions Awaiting Confirmation

### Question 1: CSH-09 Setting (B) Monetary Threshold (Pending Finance Confirmation)
- **Status**: OPEN / PENDING FINANCE CONFIRMATION.
- **Current Behavior**: Setting (B) is unconfigured (`ThresholdAmount = NULL`). The fail-safe rule is actively enforced: any cash outflow, reimbursement, or budget override exceeding **Limit (A)** (`₱10,000.00`) strictly mandates the in-person CEO/President approval record.
- **Action Needed**: Finance confirmation on the final monetary threshold value above which CEO/President approval is required.

### Question 2: R5.11 Item 7: DMS Client Option
- Retained as open per user instruction. DMS handoff documentation produced in [`docs/prompt-set-2/dms-handoff.md`](file:///c:/Users/Joana/Downloads/another%20one/CAPSTONE2/docs/prompt-set-2/dms-handoff.md).

---

## Resolved Questions (Logged in DECISIONS.md)

1. **Conflict rows 1 to 11**: All approved as recommended.
2. **Conflict row 12**: Approved with identity fields + salary rate preserved, Position + FOMS role, server-side validation.
3. **CSH-09 provisional value**: `₱10,000.00 PHP` (provisional).
4. **Attachment size limit**: 10 MB per file.
5. **R4.11 Items 1-4**: Gross expenses, no input VAT, no 1200 (switchable); R4.6 COA draft; credit memo client credit balance; Head Accountant approves FM adjusting entries.
6. **R5.11 Items 1-6**: Approved per Revision 5 addendum.
7. **Aging buckets**: `Not Yet Due`, `Due Soon (configurable)`, `1-30`, `31-60`, `61-90`, `90+`.
8. **Go-Live Opening Balances**: Opening balances only, no backfill.
9. **Existing Invoices Journal Entries**: Count is 0. No changes before Phase 19 dry run.
10. **Phase 13 EF Migration**: Additive and reversible with `Up` and `Down`. Backup taken before applying.
11. **Position Dry Run**: Only finance roles filled; others NULL.
12. **CEO Deprecation**: Deprecate with `TODO(Phase 14)`, remove from UI dropdowns, block account creation.
13. **Temporary Password Expiry**: 72 hours, server-enforced via `MustChangePasswordFilter`.
14. **Phase 14 Setting (B) Threshold Architecture**: Dedicated setting with no seeded default (`NULL`), fail-safe to Limit (A) (`₱10,000.00`), immutable trigger-protected tables, insert-only maker-checker confirmations.
15. **Phase 15 Departments & Budget Governance**:
    - Canonical codes: `DEP-FIN`, `DEP-CCS`, `DEP-DSP`, `DEP-FWD`, `DEP-OVH` (Overhead, FM-only).
    - `DEP-OPS` legacy only, not selectable for new requests; 4 existing rows in `CashAdvances` preserved.
    - Option A suggestion chip (requester's own last department, no pre-select, no server default, Overhead hidden from non-FM).
    - `dbo.DepartmentChangeLogs` insert-only table with immutability trigger; reason required; FM-only change pre-approval triggers budget re-check; locked post-approval.
    - Overhead budget line exists per category per month, allocated by FM; no seeded amounts.

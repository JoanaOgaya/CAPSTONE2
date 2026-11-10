# Prompt Set 2: Open Questions Waiting for User Decision

Status updated: 2026-11-10

---

## Active Questions Awaiting Confirmation

### Question 1: Temporary Password Expiry Duration
- **Proposal**: 72 hours (3 days) from creation time. If not used within 72 hours, the account remains locked until an administrator resets it.
- **Alternative**: 24 hours or 7 days.
- *Recommendation*: **72 hours**. Please confirm or specify preferred duration.

### Question 2: CSH-09 CEO/President Approval Limit Value (for Phase 14)
- **Context**: 
  - `dbo.ApprovalThresholdSettings` has `maximum_amount = 10,000.00` for Assistant of Finance Manager.
  - Revision 3 CSH-09 specifies that above a certain limit, CEO/President sign-off (in-person approval record) is required.
  - Phase 7 previously removed `50,000.00m` fallback literals in `ApprovalsController`.
- *Question*: What is the monetary threshold above which the CEO/President approval record is required? (e.g., transactions > ₱50,000.00, or all transactions > ₱10,000.00 that exceed Asst FM authorization).
- *Recommendation*: Keep Assistant FM limit = `₱10,000.00`. If CEO/President approval record applies to all transactions escalated above Asst FM, CEO threshold is `₱10,000.00`; if a three-tier model is intended (Asst FM $\le$ 10k, FM 10k–50k, CEO > 50k), confirm `₱50,000.00`. (User noted: "Do not change thresholds. I will answer.")

### Question 3: R5.11 Item 7: DMS Client Option
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

# Phase 18 Report: Cash Advance, Liquidation, Reimbursement, Direct Disbursement, and Net Cash

## 1. Executive Summary
Phase 18 successfully implemented and verified the end-to-end Cash Advance, Liquidation, Reimbursement, Direct Disbursement, and Net Cash workflows (`CSH-01` to `CSH-12`, Flow B) adhering strictly to the user's routing directives, separation of duties, and audit compliance requirements.

All 4 test suites pass cleanly across all system components (637 total automated tests):
- **FOMS Backend (`FOMS.Tests`)**: 401 passed, 0 failed, 0 skipped
- **AI Gateway (`foms-ai-gateway.Tests`)**: 40 passed, 0 failed, 0 skipped
- **Frontend (`foms-frontend`)**: 16 test files, 165 passed, 0 failed
- **AI Service (`ai-service`)**: 31 passed, 0 failed

---

## 2. Implemented Capabilities & Routing

### A. Cash Advance Lifecycle (`CSH-01` to `CSH-05`, `CSH-09`)
- **Request**: Submitter specifies Courier Name, Trip Reference, Amount, Department Code, Location Type, and Due Date.
- **Approval (`Committed`)**: Finance Manager approves cash advances, which commits the department's budget line (`line.CommittedAmount += advance.AmountGiven`). Requesters cannot approve their own advance (enforced server-side via SoD check).
- **Release (`Actual Outflow`)**:
  - Cash advance release records an official `CashOutflow`.
  - When the advance amount exceeds the active `CSH-09` threshold (fail-safe to Limit (A) `₱10,000.00` when unset), the server strictly enforces the presence of an approved, active `CeoApprovalRecord` (`Item 6: CashOutflow`).
  - Only the Finance Manager can record the CEO/President in-person approval tick.

### B. Liquidation Lifecycle (`CSH-06` to `CSH-08`)
- **Validation Authority**: Validated by the Assistant of Finance Manager (or Finance Manager).
- **Late Submission**: Late submissions are identified by the dynamic `isLate` boolean flag computed against `DueDate`, never mutating the canonical document status (`Pending` -> `Validated` / `For Correction` / `Rejected`).
- **Leftover Cash Returned**: Leftover cash (`advance.AmountGiven - totalExpenses`) is strictly recorded as an independent cash inflow (`CashInflow`), never deducted from disbursements.
- **Overspend Protection**: Liquidations with total expenses exceeding the cash advance amount require recorded supervisor pre-approval and original receipts before validation can proceed.
- **Rejection Reasons Dropdown**: Enforces fixed canonical reasons:
  1. `wrong receipt date or amount`
  2. `manual fuel receipt`
  3. `late submission`
  4. `other with remarks` (requires non-empty remarks)
  Returns status `"For Correction"` without posting any ledger entries, allowing couriers to resubmit.
- **Frontend Integration**: Updated [LiquidationValidation.tsx](file:///C:/Users/Joana/Downloads/another%20one/CAPSTONE2/foms-frontend/src/pages/LiquidationValidation.tsx) to provide the canonical rejection reason dropdown, required remarks for "other with remarks", and `"For Correction"` review action.

### C. Reimbursement Workflow (`CSH-10`, `CSH-11`)
- **Validation**: Assistant of Finance Manager validates the reimbursement claim. Submitter cannot validate their own claim.
- **Approval (`Committed`)**: Finance Manager always approves the reimbursement (`Committed` budget impact).
- **Payment (`Actual Outflow`)**: Paid status converts commitment to actual expense. For amounts exceeding the `CSH-09` threshold, the CEO/President approval gate (`Item 5: Reimbursement` and `Item 6: CashOutflow`) is strictly enforced before payment.

### D. Direct Disbursement (`CSH-12`)
- **Direct Recognition**: Operational direct disbursements (e.g., third-party emergency vehicle repairs) convert directly from `Committed` to `Actual` upon release, requiring NO liquidation.

### E. Net Cash Calculation (Direct Method & Period Chaining)
- Added `[HttpGet("api/CashFlow/net-cash")]` in `CashFlowController.cs`:
  $$\text{Net Cash} = \text{Beginning Balance} + \sum \text{Validated Inflows} - \sum \text{Validated Outflows}$$
  - **Beginning Balance & Period Chaining**:
    - Period 1 starts from `dbo.GoLiveOpeningBalances` (accounts `1010 Cash on Hand` and `1020 Cash in Bank`; account `1030 Petty Cash` mapped into `1010` per R4.6).
    - For subsequent periods (e.g., Month 2+), beginning cash strictly chains from the previous period's ending cash: $\text{Beginning Cash} = \text{Base Opening Cash} + \text{Prior Inflows} - \text{Prior Outflows}$.
  - **Inflows**: Validated client payments (`PaymentStatus == "Validated"` only) plus returned advances (`CashInflows` from leftover cash returns).
  - **Outflows**: Cash advances released + direct disbursements + paid reimbursements.

---

## 3. Verification & Test Evidence
- Created new test file: [Phase18DisbursementsLiquidationsAndNetCashTests.cs](file:///C:/Users/Joana/Downloads/another%20one/CAPSTONE2/foms-backend/FOMS.Tests/Phase18DisbursementsLiquidationsAndNetCashTests.cs) (20 passing unit tests covering all Phase 18 criteria, commitment releases, and period chaining).
- Updated [emp001Flow.test.tsx](file:///C:/Users/Joana/Downloads/another%20one/CAPSTONE2/foms-frontend/src/__tests__/emp001Flow.test.tsx) adding frontend liquidation rejection dropdown and "For Correction" flow verification.
- Verified full regression across all 401 backend unit tests, 40 AI gateway tests, 16 frontend test files (165 tests), and 31 AI service tests (637 tests total).

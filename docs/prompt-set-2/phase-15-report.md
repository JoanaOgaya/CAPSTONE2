# Phase 15 Report: Departments & Budget Control

## 1. Executive Summary
Phase 15 implements canonical department classification and strict budget governance across the FOMS application (`FIN-26`, `FIN-01`, `FIN-19`, `R5.2`, `R5.11`). It establishes canonical departments, server-enforced role restrictions on `DEP-OVH` (Company-wide Overhead, FM-only), legacy department retention (`DEP-OPS` not selectable for new requests, with zero data remapping), Option A suggestion chips (requester's own last department, no server defaulting, no pre-selection), and immutable change logging (`dbo.DepartmentChangeLogs` protected by database trigger `TR_DepartmentChangeLogs_Immutable`).

All test suites across all four system components pass with zero failures and zero regressions.

---

## 2. Test Verification Counts

| Component | Baseline (`phase11-complete`) | Phase 14 (`phase-14-done`) | Phase 15 (Current) | Status |
| :--- | :---: | :---: | :---: | :---: |
| **Backend Core** (`FOMS.Tests.dll`) | 297 | 327 | **351** | PASSED (+24 tests, including 9 Phase 15 tests and scratch migration idempotency tests) |
| **AI Gateway** (`foms-ai-gateway.Tests.dll`) | 40 | 40 | **40** | PASSED |
| **Frontend Integration** (`Vitest`) | 159 | 164 | **164** | PASSED |
| **AI Service** (`pytest 9.2.0`) | 31 | 31 | **31** | PASSED |
| **Grand Total Tests** | **527** | **562** | **586** | **ALL GREEN (0 failed, 0 skipped)** |
| **Frontend Lint** (`eslint`) | 0 errors | 0 errors | 0 errors | CLEAN |

---

## 3. Department Governance & Architecture

### A. Canonical Departments
Five canonical departments are established in `dbo.Departments`:
1. `DEP-FIN`: Finance & Accounting (`is_overhead = 0, is_legacy = 0`)
2. `DEP-CCS`: Coordinator & Customer Service Team (`is_overhead = 0, is_legacy = 0`)
3. `DEP-DSP`: Dispatch Team (`is_overhead = 0, is_legacy = 0`)
4. `DEP-FWD`: Forwarding Team (`is_overhead = 0, is_legacy = 0`)
5. `DEP-OVH`: Company-wide (Overhead) (`is_overhead = 1, is_legacy = 0`, FM-only selectable)

### B. Legacy Retention & Data Protection
- `DEP-OPS` (Operations) is flagged as legacy (`is_legacy = 1`).
- Existing database records (e.g. 4 legacy rows in `CashAdvances`) are preserved intact without any automatic or silent remapping.
- `DEP-OPS` cannot be selected on any new expense request (Cash Advance, Direct Disbursement, Reimbursement).

### C. Company-wide Overhead (`DEP-OVH`) FM-Only Enforced
- Non-Finance Manager users are prevented from selecting `DEP-OVH` on expense requests; attempts throw a server-side `UnauthorizedAccessException`.
- Overhead budget lines (`DEP-OVH`) can only be drafted and allocated by the Finance Manager (`FIN-01`, `R5.11 Item 6`).
- Budget line for Overhead is created without seeded amounts, allocated exclusively by the Finance Manager upon CEO/President approval record.

### D. Suggestion Chip (Option A)
- Suggestion is based strictly on the requester's own last approved/submitted department (`Option A`).
- Suggestion is a non-binding clickable chip; the department dropdown is NEVER pre-selected by default.
- Server strictly rejects empty/null departments and never defaults or auto-fills on submit.
- Non-FM users never receive `DEP-OVH` as a suggestion chip even if their previous record used it.

### E. Pre-Approval Department Correction & Immutability Audit
- Only the Finance Manager may adjust a request's department prior to approval.
- Any change requires a mandatory non-empty reason (`department_change_reason`).
- Pre-approval change triggers an automatic budget line re-check against the target department.
- Every change is recorded in `dbo.DepartmentChangeLogs` (`LogId`, `RequestType`, `RequestId`, `OldDepartmentCode`, `NewDepartmentCode`, `Reason`, `ChangedBy`, `ChangedAt`).
- `dbo.DepartmentChangeLogs` is protected by database trigger `TR_DepartmentChangeLogs_Immutable`, prohibiting all `UPDATE` and `DELETE` operations.
- Once a request is approved, the department is permanently locked (`IsDepartmentLocked = true`) and cannot be altered.

---

## 4. Phase 14 Follow-Up Checks Resolved

1. **Item 7 Discount Gate**:
   - Discount gate (`CeoApprovalItemTypes.InvoiceDiscount`) wired in `FOMS.Api/Controllers/InvoicesController.cs` and `AccountantController.cs` for invoice creation and updates applying promotional/commercial discount lines.
2. **Item 2 vs Item 3 Gate Separation**:
   - Budget transfers evaluate `CeoApprovalItemTypes.BudgetTransfer` while carry-overs evaluate `CeoApprovalItemTypes.BudgetCarryOver`, both requiring Head Accountant confirmation.
3. **Reimbursement Gate**:
   - `DisbursementAndReimbursementService.ApproveReimbursementAsync` strictly enforces threshold gate check without any direct controller bypass.
4. **Snapshot Check**:
   - Tested EF Core migration add produces an empty migration, verifying `ApplicationDbContextModelSnapshot` is in 100% synchronization with domain entities.

---

## 5. Files Changed & Added in Phase 15

### Database & Migrations:
- `foms-backend/FOMS.Infrastructure/Migrations/20261117190000_AddDepartmentControlAndChangeLogs.cs` (additive migration with Up, Down, trigger, and idempotency guards)
- `foms-backend/FOMS.Infrastructure/Migrations/ApplicationDbContextModelSnapshot.cs` (synchronized model snapshot)

### Backend Domain & Application:
- `foms-backend/FOMS.Domain/Constants/FomsDepartments.cs` (canonical codes, names, normalization, role-based restrictions)
- `foms-backend/FOMS.Domain/Entities/SqlServer/DepartmentChangeLog.cs` (audit log entity)
- `foms-backend/FOMS.Domain/Entities/SqlServer/FomsMssqlEntities.cs` (added `IsOverhead`, `IsLegacy` to `Department`; added `IsDepartmentLocked`, `DepartmentChangeReason` to request entities)
- `foms-backend/FOMS.Application/DTOs/DepartmentDtos.cs` (department and change request DTOs)
- `foms-backend/FOMS.Application/Interfaces/IDepartmentService.cs` (service contract)
- `foms-backend/FOMS.Application/Services/DepartmentService.cs` (Option A suggestion, normalization, FM change logic, budget re-check)
- `foms-backend/FOMS.Application/Services/BudgetService.cs` (Overhead allocation governance, carry-over gate)
- `foms-backend/FOMS.Application/DependencyInjection.cs` (service registration)
- `foms-backend/FOMS.Api/Controllers/DepartmentsController.cs` (department endpoints: list, suggestion, change, audit logs)
- `foms-backend/FOMS.Api/Controllers/InvoicesController.cs` (Phase 14 discount gate wiring)
- `foms-backend/FOMS.Api/Controllers/AccountantController.cs` (Phase 14 discount gate wiring)
- `foms-backend/FOMS.Infrastructure/Persistence/ApplicationDbContext.cs` & `FomsDbContext.cs` (DbSets for `Departments` & `DepartmentChangeLogs`)

### Frontend:
- `foms-frontend/src/services/departmentsService.ts` (API client for department endpoints)
- `foms-frontend/src/types/approvals.ts` (department types, lock flags, change reasons)
- `foms-frontend/src/pages/CashAdvanceLiquidationPortal.tsx` (Option A suggestion chip, canonical dropdown)
- `foms-frontend/src/pages/MyCashAdvances.tsx` (Option A suggestion chip, canonical dropdown)

### Tests & Documentation:
- `foms-backend/FOMS.Tests/Phase15DepartmentsAndBudgetControlTests.cs` (9 comprehensive unit and integration tests)
- `docs/prompt-set-2/DECISIONS.md` (all decisions and confirmations logged)
- `docs/prompt-set-2/OPEN_QUESTIONS.md` (updated open items)
- `docs/prompt-set-2/PROGRESS.md` (updated test counts and progress)

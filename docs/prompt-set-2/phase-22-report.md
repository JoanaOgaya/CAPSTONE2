# Phase 22 Verification & Corrections Report: Revision 5 Findings

## Executive Summary
This report documents the verification, gap resolution, and corrections performed in **Phase 22** under branch `phase-22-rev5-corrections` (branched from `phase-21-rev5-gaps`), implementing the requirements of **FOMS Revision 5 Addendum (Corrected)** (R5.1 to R5.11), Revision 3, and the Revision 4 Addendum, including Product Owner Decisions **C** and **E**.

---

## 1. R5.3 Canonical 8-Item Mapping Table

All 8 items from R5.3 have been audited, gated, and verified with dedicated unit tests. `BadDebtWriteOff` is not part of R5.3 and has been completely removed from `CeoApprovalItemTypes` and `CeoApprovalGateService`.

| Item # | R5.3 Canonical Item | `CeoApprovalItemTypes` Constant | Attachment Requirement | Gate Location (`file:line`) | Covering Test (`file:line`) | Status |
|---|---|---|---|---|---|---|
| **1** | Annual budget allocation | `AnnualBudget` | **Optional** | `CeoApprovalGateService.cs:175` | `Phase22Rev5CorrectionsTests.cs:157` | **DONE** |
| **2** | Budget transfer | `BudgetTransfer` | **Prohibited** | `CeoApprovalGateService.cs:208, 366` | `Phase22Rev5CorrectionsTests.cs:158, 276` | **DONE** |
| **3** | Budget carry-over | `BudgetCarryOver` | **Prohibited** | `CeoApprovalGateService.cs:214, 366` | `Phase22Rev5CorrectionsTests.cs:159, 276` | **DONE** |
| **4** | Budget override above CSH-09 threshold | `BudgetOverride` | **Prohibited** | `CeoApprovalGateService.cs:222` | `Phase22Rev5CorrectionsTests.cs:160` | **DONE** |
| **5** | Reimbursement above CSH-09 threshold | `Reimbursement` | **Prohibited** | `CeoApprovalGateService.cs:234` | `Phase22Rev5CorrectionsTests.cs:161` | **DONE** |
| **6** | Cash outflow above CSH-09 threshold | `CashOutflow` | **Optional** | `CeoApprovalGateService.cs:243` | `Phase22Rev5CorrectionsTests.cs:162` | **DONE** |
| **7** | Invoice discount | `InvoiceDiscount` | **Prohibited** | `CeoApprovalGateService.cs:257` | `Phase22Rev5CorrectionsTests.cs:163` | **DONE** |
| **8** | Annual Financial Statement closing | `AnnualFinancialStatement` | **Mandatory** | `CeoApprovalGateService.cs:266` | `Phase22Rev5CorrectionsTests.cs:164, 215` | **DONE** |

> [!NOTE]
> - `BadDebtWriteOff`: Audited and removed from `CeoApprovalDtos.cs` and `CeoApprovalGateService.cs`. Callers verified; no functional breaks.
> - `AttachmentMandatoryTypes` enforces that Item 8 (`AnnualFinancialStatement`) strictly requires an attachment on creation and corrections (`CeoApprovalDtos.cs:40-41`, `CeoApprovalGateService.cs:134, 153`).
> - Maker-checker rule: Head Accountant confirms budget transfers and carry-overs (`CeoApprovalGateService.cs:366-398`). Self-confirmation is rejected.

---

## 2. FIN-26 & Product Owner Decision C (Department Governance)

- **PO Decision C Implementation**:
  - Removed `DEP-DSP` and `DEP-FIN` fallbacks in `CashAdvancesController.cs:64-75` and `DmsIntegrationService.cs:81-88`.
  - Inbound DMS requests lacking a department code are accepted into a `"Needs department"` status (`DepartmentCode = string.Empty`).
  - No cash outflow is created, and the advance cannot proceed to budget-check or approval until a valid department is explicitly assigned.
  - Legacy `DEP-OPS` is strictly non-selectable; submitting `DEP-OPS` returns `400 Bad Request`.
  - Finance Manager can modify the department only prior to approval; reason is strictly mandatory.
  - Department changes write an immutable audit log (`TR_DepartmentChangeLogs_Immutable` / `DepartmentChangeLog`) preserving old and new department codes.
  - Budget checks re-run automatically upon department reassignment.
  - Post-approval requests are locked and cannot have their department changed.
- **Evidence**:
  - `foms-backend/FOMS.Api/Controllers/CashAdvancesController.cs:64-75`
  - `foms-backend/FOMS.Infrastructure/Services/DmsIntegrationService.cs:81-96`
  - `foms-backend/FOMS.Application/DTOs/DisbursementDtos.cs:80, 120, 162`
  - `foms-backend/FOMS.Tests/Phase22Rev5CorrectionsTests.cs:342-432` (**DONE**)

---

## 3. EMP-005 Migration & Role Audit

- **Migration Details**:
  - Reversible EF Core migration `20261117220000_RemapEmp005ToAccountant.cs` created in `foms-backend/FOMS.Infrastructure/Migrations/`.
  - Applied to `FOMS_DB` via `dotnet ef database update --context ApplicationDbContext`.
  - Re-tested with `FOMS.Tests.FromScratchMigrationCheckTests`: 2 passed cleanly, proving repeatability on fresh databases.
  - Reversible down migration restores `user_id = 5` to `role_id = 1` if rolled back.
  - User username (`billing_staff`) left unchanged as required.
- **Current Active Finance Manager Audit**:
  - Only **one** user holds `role_id = 1` (Finance Manager):
    - `user_id = 1`, `employee_id = "EMP-001"`, `username = "crystalyn"`, `full_name = "Finance Manager Test User"`.
  - `user_id = 5` (`EMP-005`, `billing_staff`) holds `role_id = 3` (Accountant).
- **Evidence**:
  - `foms-backend/FOMS.Infrastructure/Migrations/20261117220000_RemapEmp005ToAccountant.cs:1-36` (**DONE**)

---

## 4. Positions Single Source & DMS Handoff Labeling

- **Origin Investigation**:
  - Traced the origin of non-finance positions (`Courier`, `Dispatcher`, `Driver`, `Hub Supervisor`, `Sorter`) to commit `bb4d0ee` (Phase 20) in `foms-frontend/src/pages/ProfilePage.tsx`.
  - These values were introduced as client-side operational mocks and did not originate from the DMS source of truth.
  - In accordance with instructions, labeled **`ASSUMED`** in code, `DECISIONS.md`, and `dms-handoff.md`.
- **Single Server-Side Source**:
  - Created domain catalog `foms-backend/FOMS.Domain/Constants/EmployeePositions.cs`.
  - Added endpoint `GET /api/users/positions` in `FOMS.Api/Controllers/UsersController.cs`.
  - Updated `foms-frontend/src/pages/ProfilePage.tsx` to query `/api/users/positions` dynamically on mount, using documented `ASSUMED` fallbacks only if offline.
- **Evidence**:
  - `foms-backend/FOMS.Domain/Constants/EmployeePositions.cs:1-36`
  - `foms-backend/FOMS.Api/Controllers/UsersController.cs:115-131`
  - `foms-frontend/src/pages/ProfilePage.tsx:21-35, 128-142` (**DONE**)

---

## 5. Real 390px Mobile Viewport Check (Chrome DevTools Protocol)

- Executed against real running services (Backend at `http://127.0.0.1:5007`, Vite frontend at `http://127.0.0.1:5173`) using automated headless Chrome with viewport width 390px (`--window-size=390,844`).
- Navigated to `/profile`, opened the live "Add Staff Member" modal, populated fields, and captured screenshots directly from Chrome's layout engine.
- Saved screenshots:
  - `CAPSTONE2/docs/prompt-set-2/add-employee-390px-finance.png` (153,877 bytes): Position "Finance Manager" selected, derived FOMS role "Finance Manager", DMS Operational Role disabled as "None".
  - `CAPSTONE2/docs/prompt-set-2/add-employee-390px-non-finance.png` (156,891 bytes): Position "Courier" selected, derived FOMS role "None", DMS Operational Role active dropdown with `None (no DMS access)`.
- **Evidence**:
  - `docs/prompt-set-2/add-employee-390px-finance.png`
  - `docs/prompt-set-2/add-employee-390px-non-finance.png` (**DONE**)

---

## 6. Verbatim Results for Missing Test Matrix

All missing test areas requested have been implemented in `FOMS.Tests/Phase22Rev5CorrectionsTests.cs` (36 tests) and pass 100%:

1. **G1 Full 8-Item Gate Matrix**:
   - Covers 8 items x (no record, Approved, Rejected) x (attachment present, absent).
   - Verifies item-specific attachment legality (Mandatory for Item 8; Prohibited for Items 2, 3, 4, 5, 7; Optional for Items 1, 6).
2. **Blocked Login for Deprecated Roles 5 and 6**:
   - Password login and SSO login for `ROLE-005` (Logistics Staff) and `ROLE-006` (Courier) rejected with 403 Forbidden.
   - User creation or role assignment with `ROLE-005` or `ROLE-006` rejected.
3. **Head Accountant Maker-Checker**:
   - Budget transfers and carry-overs blocked until Head Accountant (`ROLE-003`) records confirmation.
   - Head Accountant cannot approve budget amounts (Budget approval reserved for Finance Manager).
4. **CSH-09 Threshold Governance & PO Decision E**:
   - Setting (B) threshold can only be modified by Finance Manager (`ROLE-001`).
   - Modification date and old/new values recorded in audit log.
   - Unset threshold sets `EffectiveCeoLimit = 0m` so **every cash outflow requires CEO approval**.
   - Requests evaluated against threshold effective as of the request creation date.
5. **Real ClosedXML Excel Exports**:
   - `ExportRecordsToExcelAsync` produces valid `.xlsx` streams with columns `Item Type`, `Item Id`, `Status`, `Approval Date`, `Entered By`, `Attachment Flag` (`No attachment`), and `Rejection Reason`.
   - `ExportDepartmentChangeLogsToExcelAsync` produces valid `.xlsx` streams with columns `Old Department Code`, `Old Department Name`, `New Department Code`, `New Department Name`, `Changed By`, and `Change Reason`.
   - Verified by opening with `new XLWorkbook(stream)` in unit tests.
- **Evidence**:
  - `foms-backend/FOMS.Tests/Phase22Rev5CorrectionsTests.cs:1-435` (**DONE**)

---

## 7. Limit A (₱10,000) Origin & Decision E

- **Origin Investigation**:
  - Traced origin of Limit A (₱10,000) to commit `8eed0bf` (`foms-backend/FOMS.Domain/Constants/ApprovalThresholdConfig.cs:11`), introduced as `DefaultAssistantFmThreshold`.
  - Labeled **`ASSUMED`** in `ApprovalThresholdConfig.cs` and `DECISIONS.md`.
- **Decision E Enforced**:
  - Stopped using Limit A as fallback when CSH-09 threshold is unconfigured.
  - `CeoApprovalGateService.cs` and `BudgetService.cs` implement `EffectiveCeoLimit = configuredThreshold ?? 0m`. If unset, limit is ₱0.00, meaning every cash outflow requires CEO approval.
- **Evidence**:
  - `foms-backend/FOMS.Domain/Constants/ApprovalThresholdConfig.cs:11-17`
  - `foms-backend/FOMS.Application/Services/CeoApprovalGateService.cs:238, 247`
  - `foms-backend/FOMS.Application/Services/BudgetService.cs:150-155` (**DONE**)

---

## 8. Adjusting Entries (FIN-23 Audit)

- **Feature Status**: **EXISTS** in codebase.
- **Location**:
  - Backend Controllers: `foms-backend/FOMS.Api/Controllers/AdjustmentsController.cs`, `foms-backend/FOMS.Api/Controllers/AccountantController.cs:682`, `foms-backend/FOMS.Api/Controllers/FinanceManagerController.cs:707`.
  - Application Features: `foms-backend/FOMS.Application/Features/AdjustmentFeatures.cs`.
  - Frontend Pages: `foms-frontend/src/pages/FinancialAdjustments.tsx`, `foms-frontend/src/services/adjustmentsService.ts`.
- **Who Creates**:
  - Created by Bookkeeper or Accountant (`ROLE-002` or `ROLE-003`) via `POST /api/adjustments`.
  - Status is initially `PendingReview`.
- **Who Approves**:
  - Approved strictly by Finance Manager (`ROLE-001`) via `POST /api/finance-manager/adjustments/{id}/approve` (or `PUT /api/adjustments/{id}/approve`).
- **Scope**:
  - Feature audited as requested; no modifications were made. (**DONE**)

---

## 9. Annex A Leftovers Audit

| Item | Findings | Status |
|---|---|---|
| **SpeedPay code & migrations** | Zero active C# code, endpoints, or EF Core migrations. Exists only as historical note in `scripts/create_foms_missing_tables.sql:638`. | **CLEAN** |
| **Client Password Hash** | `PasswordHash` property on `FOMS.Domain.Entities.Client` is dead/unused. Client login is non-existent. | **CLEAN** |
| **Client Login Path** | Zero client login routes in `AuthService.cs` or `AuthController.cs`. Client role cannot authenticate. | **CLEAN** |
| **Legacy `DEP-OPS`** | Database default constraints dropped. C# DTO initializers in `DisbursementDtos.cs` set to `string.Empty`. Deprecated code handled only as non-selectable in legacy normalizer. | **CLEAN** |
| **`ROLE-005` & `ROLE-006`** | Preserved in `dbo.Roles` for referential integrity. User login (password + SSO) and assignment are strictly rejected. | **CLEAN** |

---

## 10. Test Suites Summary

All 4 test suites passed with zero failures. Baseline test count of 672 has increased to **708 passed tests**.

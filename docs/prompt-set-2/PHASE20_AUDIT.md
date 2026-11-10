# Phase 20 Audit Report: FOMS Revision 5 Addendum (Corrected)

- **Date**: 2026-11-10
- **Status**: Audit Complete — PAUSED FOR USER APPROVAL (Step 1)
- **Branch**: `phase-20-rev5-approvals-employees`
- **Scope**: Verification of R5.9 audit checks across database, backend, frontend, and DMS contracts.

---

## Executive Summary

This read-only audit examines the current state of FOMS and DMS integration contracts against the requirements of **FOMS Revision 5 Addendum (Corrected)** (IAM-07 to IAM-11, FIN-26, R5.2 to R5.11).

Key takeaways:
1. **CEO/President Login Elimination**: The backend currently still contains legacy `IsCeoOrPresident` methods and checks in 4 services (`BillingComputationService`, `BudgetService`, `DisbursementAndReimbursementService`, `FinancialStatementService`) that assume a CEO/President role or login exists, rather than enforcing in-person `CeoApprovalRecord` entries recorded by the Finance Manager.
2. **Old Role Names & Values**: The deprecated role names `"Financial Manager"` and `"Assistant Financial Manager"` remain in `dbo.Roles` (description), `dbo.Users` (test user full names), and in fallback checks in 5 backend controllers/services. Table `dbo.Users` also contains an obsolete `Client` role row (`user_id = 8`, `CLIENT-001`).
3. **Database Department Column Defaults**: Tables `dbo.CashAdvances`, `dbo.DirectDisbursements`, and `dbo.Reimbursements` currently carry an obsolete SQL Server default constraint `('DEP-OPS')`, and C# entities initialize to `"DEP-OPS"`, contrary to FIN-26 which mandates that department starts empty with no default.
4. **Server-Side Role/Position Validation**: `UserService.cs` validates finance position matches role, but does not yet mandate `Position` for non-finance staff, nor does it enforce `FOMS role = "None"` for non-finance personnel, nor does it handle DMS operational roles.
5. **Department Change Logging**: Fully implemented, verified, and protected by database immutability trigger `TR_DepartmentChangeLogs_Immutable`.

---

## Detailed Audit per R5.9 Check

### 1. Where Departments and Roles are Stored (Dropdown, Enum or Table)

#### A. Departments
- **Database**: Stored in table `dbo.Departments`:
  - 5 active canonical departments:
    - `DEP-FIN` (Finance & Accounting, `is_overhead = 0, is_legacy = 0`)
    - `DEP-CCS` (Coordinator & Customer Service Team, `is_overhead = 0, is_legacy = 0`)
    - `DEP-DSP` (Dispatch Team, `is_overhead = 0, is_legacy = 0`)
    - `DEP-FWD` (Forwarding Team, `is_overhead = 0, is_legacy = 0`)
    - `DEP-OVH` (Company-wide (Overhead), `is_overhead = 1, is_legacy = 0`)
  - 5 legacy departments marked `is_legacy = 1`:
    - `DEP-OPS` (Operations), `DEP-FLT` (Fleet & Logistics), `DEP-HR` (Human Resources), `DEP-IT` (Information Technology), `DEP-EXEC` (Executive Office).
- **Backend Model & Constants**:
  - `FOMS.Domain.Entities.SqlServer.Department` in `FomsMssqlEntities.cs`.
  - `FOMS.Domain.Constants.FomsDepartments` and `FOMS.Application.DTOs.DepartmentDtos.DepartmentCodes`.
  - API: `GET /api/departments` in `DepartmentsController.cs`.
- **Frontend**:
  - Fetched dynamically from `/api/departments` via `src/services/departmentsService.ts`.
  - Rendered in `<select>` dropdowns in `RecordCashOutflowModal.tsx` and `CashAdvanceLiquidationPortal.tsx`. Overhead (`DEP-OVH`) is restricted to Finance Manager only.

#### B. Roles
- **Database**:
  - Stored in table `dbo.Roles` (6 rows):
    - `ROLE-001`: Name = `Finance Manager`, role_name = `Finance Manager`
    - `ROLE-002`: Name = `Head Accountant`, role_name = `Head Accountant`
    - `ROLE-003`: Name = `Accountant`, role_name = `Accountant`
    - `ROLE-004`: Name = `Assistant of Finance Manager`, Description = `Assistant Financial Manager`, role_name = `Assistant of Finance Manager`
    - `ROLE-005`: Name = `Billing & Collection Coordinator`, role_name = `Billing & Collection Coordinator`
    - `ROLE-006`: Name = `Client`, Description = `External Client User`, role_name = `Client`
  - Table `dbo.Users` has column `role_id` referencing `dbo.Roles.role_id`.
  - Table `dbo.Employees` has string column `Role` (e.g. `Finance Manager`) and `RoleId`.
- **Backend Code**:
  - `FOMS.Domain.Constants.FomsRoles` defines canonical strings: `FinanceManager`, `HeadAccountant`, `Accountant`, `AssistantFinanceManager`.
  - Deprecated constants: `LegacyFinancialManager = "Financial Manager"`, `LegacyAssistantFinancialManager = "Assistant Financial Manager"`.
- **Frontend**:
  - `src/types/auth.ts`: Enum `FomsRole` (`FINANCE_MANAGER`, `HEAD_ACCOUNTANT`, `ACCOUNTANT`, `ASSISTANT_FM`).
  - `src/pages/ProfilePage.tsx`: `FINANCE_POSITIONS = ['Finance Manager', 'Head Accountant', 'Accountant', 'Assistant of Finance Manager']`.

---

### 2. Whether Employee Form or Table has Department Field and any Role Depends on it

- **Database Table (`dbo.Employees`)**:
  - Columns: `Id`, `Name`, `Role`, `RoleId`, `Email`, `IsActive`, `CreatedAt`, `SystemAccess`, `Status`, `Username`, `PasswordHash`, `FailedLoginCount`, `LockedAt`, `DateHired`, `Phone`, `SalaryRate`, `Position`, `MustChangePassword`, `IsTemporaryPassword`, `TemporaryPasswordExpiresAt`.
  - **Result**: **NO** `Department` column exists in `dbo.Employees`.
- **Database Table (`dbo.Users`)**:
  - Columns: `user_id`, `employee_number`, `full_name`, `username`, `password_hash`, `role_id`, `is_active`, `created_at`, `updated_at`, `failed_login_count`, `locked_at`.
  - **Result**: **NO** `Department` column exists in `dbo.Users`.
- **Employee Entity (`Employee.cs`)**:
  - **Result**: **NO** `Department` property exists.
- **Frontend Form (`ProfilePage.tsx`)**:
  - Fields present on modal: `Employee ID`, `Position`, `FOMS Role (Derived)`, `Full Legal Name`, `Email`, `Phone`, `Salary Rate`.
  - **Result**: **NO** `Department` field exists on the Add Employee modal.
- **Role Dependencies**:
  - **Result**: Zero roles depend on department. Roles are determined by `Position` (for finance roles) or assigned system roles.

---

### 3. Whether Department on Budget Lines, Cash Advances, Disbursements and Reimbursements is Required with No Default

- **MonthlyBudgetLines**:
  - Column `department_code`: `NOT NULL`, `COLUMN_DEFAULT = NULL`.
  - Service requires fiscal year, month, department code, and category. Starts empty with no default.
- **CashAdvances**:
  - Column `DepartmentCode`: `NOT NULL`, `COLUMN_DEFAULT = ('DEP-OPS')`.
  - Entity: `public string DepartmentCode { get; set; } = string.Empty;`
  - Controller requires department on submit.
  - **Gap**: Database table carries an obsolete default constraint `('DEP-OPS')`.
- **DirectDisbursements**:
  - Column `department_code`: `NOT NULL`, `COLUMN_DEFAULT = ('DEP-OPS')`.
  - Entity: `public string DepartmentCode { get; set; } = "DEP-OPS";`
  - **Gap**: Database table default is `('DEP-OPS')` and C# entity initializes to `"DEP-OPS"`.
- **Reimbursements**:
  - Column `department_code`: `NOT NULL`, `COLUMN_DEFAULT = ('DEP-OPS')`.
  - Entity: `public string DepartmentCode { get; set; } = "DEP-OPS";`
  - **Gap**: Database table default is `('DEP-OPS')` and C# entity initializes to `"DEP-OPS"`.
- **Frontend Request Modals**:
  - Modals start with empty department (`""`) and prompt `-- Select Department --`. Option A suggestion chip displays requester's last used department, but never pre-selects or forces it.

---

### 4. Old Role Names ("Financial Manager", "Assistant Financial Manager", "Client") in Code, DB Values, Labels and Permission Checks

- **"Financial Manager"**:
  - **DB Values**: `dbo.Users` row 1 (`EMP-001`) `full_name = 'Financial Manager Test User'`.
  - **Backend Code**:
    - `FomsRoles.LegacyFinancialManager = "Financial Manager"`.
    - Fallback checks in 5 backend files:
      1. `FOMS.Api\Controllers\FinancePaymentsController.cs#L50`: `role.Equals("Financial Manager", ...)`
      2. `FOMS.Api\Controllers\PaymentsController.cs#L96`: `role.Equals("Financial Manager", ...)`
      3. `FOMS.Application\Services\CeoApprovalGateService.cs#L43`: `role.Equals("Financial Manager", ...)`
      4. `FOMS.Application\Services\DepartmentService.cs#L28`: `string.Equals(role, "Financial Manager", ...)`
      5. `FOMS.Domain\Constants\FomsDepartments.cs#L92`: `string.Equals(userRole, "Financial Manager", ...)`
  - **Frontend**: Clean (0 occurrences).
- **"Assistant Financial Manager"**:
  - **DB Values**:
    - `dbo.Roles` row 4 (`ROLE-004`) has `Description = 'Assistant Financial Manager'`.
    - `dbo.Users` row 4 (`EMP-004`) has `full_name = 'Assistant FM Test User'`.
  - **Backend Code**:
    - `FomsRoles.LegacyAssistantFinancialManager = "Assistant Financial Manager"`.
  - **Frontend**: Clean (0 occurrences).
- **"Client"**:
  - **DB Values**:
    - `dbo.Roles` row 6 (`ROLE-006`): `Name = 'Client'`, `Description = 'External Client User'`.
    - `dbo.Users` row 8 (`CLIENT-001`): `role_id = 6` (`Client`), `username = 'client.shopee'`.
    - `dbo.Employees`: Clean (0 client rows).
  - **Backend Code**:
    - `AuthService.LoginAsync` rejects any client credentials (IAM-06). `FomsRoles.AllCanonicalRoles` excludes Client.
  - **Frontend**: Clean (no Client role in FOMS forms).

---

### 5. Employee Rows with a Client Role or a FOMS Role That Does Not Match the Position

- **`dbo.Employees`**:
  - All 4 active employees have exactly matching Position and Role:
    - `EMP-001`: `Role = 'Finance Manager'`, `Position = 'Finance Manager'` (Match)
    - `EMP-002`: `Role = 'Head Accountant'`, `Position = 'Head Accountant'` (Match)
    - `EMP-003`: `Role = 'Accountant'`, `Position = 'Accountant'` (Match)
    - `EMP-004`: `Role = 'Assistant of Finance Manager'`, `Position = 'Assistant of Finance Manager'` (Match)
  - **Result**: Zero Client rows, zero mismatches in `dbo.Employees`.
- **`dbo.Users`**:
  - User 5 (`EMP-005`): `full_name = 'Billing Staff Test User'`, `role_id = 1` (`Finance Manager`). Mismatch between title and role.
  - User 8 (`CLIENT-001`): `full_name = 'Test Client Company User'`, `role_id = 6` (`Client`). Deprecated client user row.

---

### 6. Any Approval Step That Assumes a CEO/President Login

- **Backend Violations Found**:
  - `BillingComputationService.cs` (L177-178): Checks `norm.Contains("ceo") || norm.Contains("president")` to approve discounts.
  - `BudgetService.cs` (L38, L49, L240, L280, L497, L655, L738): Contains `IsCeoOrPresident(string role)` checking `userRole` for `"ceo"` or `"president"` to authorize annual budget, budget transfers, overrides, and carry-overs.
  - `DisbursementAndReimbursementService.cs` (L39, L52, L188, L511, L548): Checks `IsCeoOrPresident(userRole)` and `advance.ApprovedBy != "EMP-CEO"`.
  - `FinancialStatementService.cs` (L32, L43, L608): Checks `IsCeoOrPresident(userRole)` to approve AFS.
- **Correction Required**:
  - Remove all `IsCeoOrPresident` methods and login role checks.
  - Enforce gating strictly against `CeoApprovalRecord` (status = Approved, entered by Finance Manager, effective date check, second-person confirmation hook).

---

### 7. Whether Server Validates Role Against Position

- **Current Implementation in `UserService.cs`**:
  - Derives `Role` from `Position` if `Position` is a known finance role.
  - Throws `InvalidOperationException` if `Role` is provided and does not match `Position`.
- **Gaps Identified**:
  - `Position` is not strictly enforced as required on non-finance staff.
  - Does not enforce `FOMS Role = "None"` for non-finance positions.
  - Does not support the 3rd field: `DMS operational role` (`Admin`, `Encoder`, `Driver`, `None (no DMS access)`).

---

### 8. Whether a Department Change on a Request is Logged

- **Implementation**: **Fully Implemented and Verified**.
  - Table: `dbo.DepartmentChangeLogs` (`LogId`, `RequestType`, `RequestId`, `OldDepartmentCode`, `NewDepartmentCode`, `Reason`, `ChangedBy`, `ChangedAt`).
  - Trigger: `TR_DepartmentChangeLogs_Immutable` active and blocks `UPDATE` and `DELETE`.
  - Logic: `DepartmentService.ChangeRequestDepartmentAsync` allows changes only pre-approval, by Finance Manager only, with detailed reason required, re-runs budget availability, shifts commitment reservations, and writes immutable change log.

---

### 9. DMS Audit: Pre-selected Encoder, Missing "None (no DMS access)", and Consignor Registration

- **Pre-selected Encoder**:
  - Documented in `docs/prompt-set-2/dms-handoff.md`. DMS must remove the pre-selected `Encoder` role.
- **Missing "None (no DMS access)"**:
  - Documented in `docs/prompt-set-2/dms-handoff.md`. DMS operational role dropdown must include explicit `None (no DMS access)`.
- **DMS Client / Consignor Registration**:
  - Verified: FOMS has no client portal or login (IAM-06). If DMS registers external consignor users, the `Client (Consignor / Order Creator)` role belongs strictly to DMS, completely omitted from FOMS forms and operations. Tracked under R5.11 item 7.

---

## Action Plan for Step 2 (Upon User Approval)

1. **Reversible SQL Migration**:
   - Drop default constraint `('DEP-OPS')` on `CashAdvances.DepartmentCode`, `DirectDisbursements.department_code`, and `Reimbursements.department_code`.
   - Update `dbo.Roles`: change `ROLE-004` Description to `"Assistant of Finance Manager"`; mark `ROLE-006` (`Client`) as inactive/deprecated.
   - Update `dbo.Users`: update `EMP-001` full name to `"Finance Manager Test User"`; remap `EMP-005` or align role; deactivate `CLIENT-001`.
2. **Backend CEO/President Gate Refactoring (IAM-07 to IAM-10)**:
   - Remove all `IsCeoOrPresident` role-checking methods.
   - Refactor the 8 gated items to query `CeoApprovalRecords` entered by Finance Manager:
     1. Annual budget allocation
     2. Budget transfer
     3. Carry-over
     4. Budget override above CSH-09 threshold
     5. Reimbursement above CSH-09 threshold
     6. Cash outflow release above CSH-09 threshold
     7. Invoice discount line
     8. Annual financial statements package
   - Support `RejectionReason` on rejected records and release reservations for rejected advances/reimbursements.
   - Support CSH-09 setting threshold lookup in force on request date.
   - Add hook for Head Accountant confirmation of transfers and carry-overs (logged as open question per R5.11 item 1).
3. **Backend & Frontend Add Employee Form Alignment (IAM-11, R5.6)**:
   - Server validates 3 fields: `Position` (required, non-empty), `FOMS role`, `DMS operational role`.
   - If Position is a finance position: FOMS role derived and read-only, DMS role = `"None"`.
   - If Position is non-finance: FOMS role = `"None"`, DMS role = `Admin`, `Encoder`, `Driver`, or `None (no DMS access)`.
   - Mobile-responsive stacked layout (1 field per row).
4. **Verification & Tests**:
   - Unit and integration tests for all 8 gates, immutability, date restrictions, FM-only recording, department validation, and role-position derivation.
